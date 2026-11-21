---
title: Proud zpráv (stream)
summary: Fronta vs. log – proud minishop.events v RabbitMQ, ze kterého se čte od záložky (času) a nic se nemaže; stránka Proud zpráv ukáže celou pouť objednávky napříč moduly.
order: 14
---

> **V jedné větě:** Kromě front, ze kterých si moduly zprávy **berou** (a zprávy mizí), teď každá zpráva skončí i v **proudu**
> `minishop.events` – deníku, ze kterého se jen **čte** od zvolené záložky a nic se nemaže (až po 14 dnech).

## Fronta vs. proud (log)

| | Fronta (dosud) | Proud / log (od kroku B1) |
| --- | --- | --- |
| Přirovnání | schránka na dopisy – vyzvednuto = pryč | kniha jízd – zápisy zůstávají, každý čtenář má svou záložku |
| Po přečtení | zpráva zmizí (ack) | zpráva zůstává; ack jen řídí, kolik zpráv broker pošle dál |
| Víc čtenářů | každý potřebuje vlastní frontu (kopii) | čtou tentýž proud nezávisle, každý od svého místa |
| Nový čtenář | dostane jen nové zprávy | může si přečíst historii (přehrání, *replay*) |
| Neúspěch | opakování, dead-letter fronta | nic takového – čtenář přeskočí nebo zastaví |
| Mazání | po zpracování | **retence**: podle stáří (14 dní) a velikosti (1 GB) |
| Na co | úkol, který má někdo udělat (rezervuj, pošli e-mail) | historie, ze které si každý vyčte, co potřebuje (audit, statistiky) |

Tohle je princip **Apache Kafka**. RabbitMQ ho umí od verze 3.9 jako typ fronty `stream` – nový broker nepotřebujeme.

**Záložka (offset)** = pořadové číslo zprávy v proudu (0, 1, 2…). Čtenář řekne, odkud chce číst: `first` (od začátku
retence), `next` (jen nové), konkrétní číslo, nebo **čas** – RabbitMQ najde blok zpráv z té doby.

**Proud není zdroj pravdy.** Pravda zůstává v databázích modulů. Proud je kopie zpráv na 14 dní – na rozdíl od
[event sourcingu košíku](event-sourcing.md), kde jsou události samy stavem a nemažou se.

## Jak to MiniShop používá

```mermaid
flowchart LR
    O[Ordering, sklad, platby, …<br/>outbox] -->|publish| X{{exchange<br/>každého kontraktu<br/>fanout}}
    X --> Q1[minishop.ordering<br/>fronta – rozdělí, smaže]
    X --> Q2[minishop.inventory …]
    X --> S[(minishop.events<br/>PROUD – 14 dní / 1 GB)]
    S -->|čtení od času,<br/>nic se nemaže| P[Stránka Proud zpráv<br/>Provoz]
```

- **Založení:** [RabbitMqEventStream](code:src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqEventStream.cs) –
  fronta s `x-queue-type: stream`, `x-max-age: 14D`, `x-max-length-bytes` 1 GB, segmenty po 50 MB (retence maže celé
  segmenty – menší = jemněji). Navázaná na exchange **všech** kontraktů. Zakládá ji Web po startu
  ([EventStreamSetupWorker](code:src/MiniShop.Infrastructure/Hosting/EventStreamSetupWorker.cs), vedoucí kopie,
  idempotentně) – nový kontrakt se tak naváže sám s nasazením kódu.
- **Čtení:** [RabbitMqEventStreamReader](code:src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqEventStream.cs) –
  vlastní kanál, `BasicQos` (proud ho vyžaduje), `BasicConsume` s argumentem `x-stream-offset` = čas. Čte, dokud chvíli
  (0,8 s) nepřijde nic – to je „konec“ – a odpojí se. Nejvýš 50 000 prohledaných a 2 000 zobrazených zpráv.
- **Stránka Proud zpráv** (`/Admin/EventStream`, role Provoz): od kdy (15 min … 7 dní, nebo čas), typ zprávy, číslo
  objednávky → tabulka: pořadí v proudu, čas faktu, typ, objednávka, JSON. Číslo objednávky ukáže celou **pouť
  objednávky napříč moduly** (OrderPlacedV2 → ReserveStockV1 → StockReservedV1 → … → OrderStatusChangedV1 →
  ShipmentStatusChangedV1 → OrderCompletedV1 → InvoiceIssuedV1) v pořadí, jak šly brokerem. Z detailu objednávky odkaz
  „Zprávy v proudu“ (jen Provoz).
- **Osobní údaje:** zprávy je nesou (jméno, e-mail, adresa kvůli e-mailům a dopravci). Stránka je ukazuje
  zamaskované – [PersonalDataMask](code:src/MiniShop.Infrastructure/Integration/PersonalDataMask.cs) podle jména pole
  (`CustomerName`, `CustomerEmail`, `RecipientName`, `DeliveryAddress`, `Street`, `City`, `PostalCode`, `Reason`,
  `…Email`). Nový kontrakt s takovým polem bez masky zastaví test `PersonalDataMaskTests`.

## Rozhodnutí

- **AMQP, ne nativní protokol streamů.** RabbitMQ má pro proudy i vlastní protokol (port 5552, plugin `rabbitmq_stream`,
  knihovna `RabbitMQ.Stream.Client`) – rychlejší, umí říct „kde proud končí“ a hlídá záložky čtenářů na serveru. Pro náš
  provoz stačí stávající AMQP knihovna bez změny nasazení; „konec“ poznáme podle ticha.
- **Počet zpráv v proudu RabbitMQ přes AMQP nehlásí** (u proudu vrací 0) – metrika pro Grafanu proto není; velikost
  proudu je vidět v RabbitMQ UI.
- **Do zálohy nepatří** – je to kopie, data jsou v databázích modulů.
- Historie začíná nasazením kroku B1; přehrát jde nejvýš 14 dní zpět.

## Trvalý čtenář se záložkou – projekce přehráním (krok B2)

Stránka Proud zpráv si proud jen **prohlédne** (přečte od času a odpojí se). Krok B2 přidal **trvalého čtenáře**: poslouchá
proud pořád a staví čtecí model **Rychlost vyřízení** (jak dlouho trvá potvrzení, expedice a doručení) – jen ze zpráv
v proudu, Ordering o něm neví.

```mermaid
sequenceDiagram
    participant S as proud minishop.events
    participant W as LeadTimeProjectionWorker (Reporting.Api)
    participant DB as schéma reporting
    W->>DB: záložka? (StreamCheckpoints: offset 41, generace 2)
    W->>S: číst od offsetu 42 (x-stream-offset = 42, bez záložky „first“)
    S-->>W: zprávy 42…141 (OrderStatusChangedV1, ShipmentStatusChangedV1, ostatní přeskočí)
    W->>DB: JEDNA transakce: OrderLeadTimes + záložka = 141 (generace musí být pořád 2)
    Note over W,DB: pád mezi dávkami → po startu znovu od 142 – nic dvakrát, nic vynechané
```

- **Záložka ve stejné transakci jako data** (`StreamCheckpoints` vedle `OrderLeadTimes`) – efekt „přesně jednou“ bez
  inboxu. Dávky po 100 zprávách nebo 1 s.
- **Přestavění = přetočit záložku:** v jedné transakci smazat data, záložku na začátek a zvýšit **generaci**. Běžící čtenář
  u další dávky uvidí jinou generaci, rozpracované zahodí a čte od `first`. Dávka uložená zrovna v tu chvíli neprojde –
  záložka má concurrency token (`Version`). Ordering se na nic neptá – srovnání s přestavěním přes snímky v [CQRS](cqrs.md).
- **Limit:** přehraje se jen to, co proud drží (14 dní). Proto proud nenahrazuje snímky – doplňuje je.
- **Nečitelná zpráva:** proud nemá dead-letter frontu – čtenář ji zaloguje, přeskočí a záložka jde dál.

Kód: [LeadTimeProjectionWorker](code:src/MiniShop.Infrastructure/Reporting/LeadTimeProjectionWorker.cs),
[LeadTimeReportService](code:src/MiniShop.Application/Reporting/LeadTimeReportService.cs),
[OrderLeadTime a StreamCheckpoint](code:src/MiniShop.Domain/Reporting/OrderLeadTime.cs); stránka `/Admin/LeadTimes`.

## Na internetu

- [RabbitMQ Streams](https://www.rabbitmq.com/docs/streams) – typ fronty, retence, `x-stream-offset`
- [Stream vs. fronta (RabbitMQ blog)](https://www.rabbitmq.com/blog/2021/07/13/rabbitmq-streams-overview)
- [Jay Kreps: The Log – what every software engineer should know](https://engineering.linkedin.com/distributed-systems/log-what-every-software-engineer-should-know-about-real-time-datas-unifying)
