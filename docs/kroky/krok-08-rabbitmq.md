# Krok 8: Message broker (RabbitMQ)

Integrační události z kroku 7 teď opravdu odcházejí ven, do RabbitMQ. Ukázkový consumer je z brokeru zase
vyzvedává a loguje. Doména, služby, handlery ani kontrakty se nezměnily. Přibyla jen nová implementace
`IIntegrationEventBus` v infrastruktuře.

## Jak to funguje

Přirovnání: broker je **pošta**. MiniShop odevzdá dopis na přepážce (publish) a dostane potvrzení o převzetí
(publisher confirm). Pošta ho roztřídí do schránek všech, kdo si danou zásilku předplatili (fronty). Adresát si
poštu vyzvedne, až má čas (consumer), a podepíše převzetí (ack). Když je adresát na dovolené, dopisy na něj
ve schránce počkají.

```
                         ┌─ exchange "OrderPlacedV1" (fanout) ─┐
outbox ─► OutboxWorker ─►│                                     ├─► fronta "minishop.audit" ─► RabbitMqAuditConsumer (log + ack)
   (krok 7)    publish + │─ exchange "OrderCancelledV1" ───────┤
               confirm   └─ exchange "OrderCompletedV1" ───────┘   (v krocích 9–10 další fronty: inventory, notifications)
```

| Pojem RabbitMQ | V MiniShopu |
| --- | --- |
| **exchange** (třídírna) | jedna na kontrakt, typ *fanout* = kopie do všech navázaných front, název = název kontraktu |
| **queue** (schránka) | `minishop.audit` – vlastní fronta ukázkového consumera |
| **binding** (předplatné) | `minishop.audit` je navázaná na všechny 3 exchange |
| **publisher confirm** | `PublishAsync` skončí, až broker zprávu převezme; teprve pak worker označí řádek outboxu |
| **ack** | consumer potvrdí zprávu až PO zpracování; když spadne dřív, broker ji doručí znovu |
| **durable / persistent** | exchange, fronta i zprávy přežijí restart brokeru |

**Spolehlivost celé cesty:** outbox (krok 4) → publisher confirm → durable fronta → ack. Na žádném úseku
se zpráva neztratí, nanejvýš se doručí dvakrát. Proto má každá zpráva `MessageId` = `EventId`, podle kterého
pozná duplicitu inbox příjemce (krok 6).

**Nedostupný broker není chyba zprávy.** `RabbitMqIntegrationEventBus` vyhodí `IntegrationBusUnavailableException`.
Worker pak zprávě nezvýší `Attempts`, zapíše jen `Error` a zbylé integrační zprávy v dávce už nezkouší (doménové
zpracuje normálně). Výpadek brokeru tak nezpůsobí poison messages. Ověřeno naostro:

```
[18:32:57 WRN] Spojení s RabbitMQ se přerušilo (fronta minishop.audit)          ← docker stop
[18:33:00 INF] Objednávka 2 vytvořena pro eva@example.com …                    ← objednávky dál fungují
[18:33:07 WRN] Broker není dostupný, integrační zprávy počkají v outboxu …
[18:33:12 INF] Připojeno k RabbitMQ localhost:5672                             ← docker start
[18:33:12 INF] [RabbitMQ] OrderPlacedV1 1525d6f6-… publikována do exchange OrderPlacedV1
[18:33:16 INF] Consumer poslouchá frontu minishop.audit
[18:33:16 INF] [Přijato z minishop.audit] OrderPlacedV1 1525d6f6-… : {...}     ← zpráva počkala ve frontě
```

**Přepínač** `Messaging:Transport` v `appsettings.json`: `RabbitMq` (výchozí) nebo `Log`. S hodnotou `Log` aplikace
běží jako v kroku 7 a Docker nepotřebuje.

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `Infrastructure/Integration/IntegrationBusUnavailableException.cs` | „pošta je zavřená“ – nepočítá se jako pokus |
| `Infrastructure/Integration/RabbitMq/RabbitMqOptions.cs` | host, port, jméno, heslo, název fronty |
| `Infrastructure/Integration/RabbitMq/RabbitMqConnection.cs` | jedno sdílené TCP spojení, při výpadku nové |
| `Infrastructure/Integration/RabbitMq/RabbitMqTopology.cs` | deklarace exchange (fanout, durable) |
| `Infrastructure/Integration/RabbitMq/RabbitMqMessage.cs` | hlavičky zprávy (`MessageId`, `Type`, persistent) + JSON tělo |
| `Infrastructure/Integration/RabbitMq/RabbitMqIntegrationEventBus.cs` | publish s publisher confirms |
| `Infrastructure/Integration/RabbitMq/RabbitMqAuditConsumer.cs` | ukázkový consumer: fronta, binding, ack/nack, znovupřipojení |
| `Web/BackgroundServices/IntegrationEventAuditWorker.cs` | hosting consumera (jako `OutboxWorker`) |
| `Web/Extensions/MessagingServiceCollectionExtensions.cs` | `AddMessaging` – volba transportu podle konfigurace |
| `tests/.../Infrastructure/RabbitMqTests.cs` | 3 testy (hlavičky, tělo, nedostupný broker) |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/MiniShop.Infrastructure.csproj` | balíček `RabbitMQ.Client` 7.2.1 |
| `Infrastructure/DependencyInjection.cs` | nová `AddRabbitMq(options)` – nahradí logovací bus |
| `Infrastructure/Outbox/OutboxProcessor.cs` | `IntegrationBusUnavailableException` se nepočítá jako pokus; zbylé integrační zprávy v dávce přeskočí |
| `Infrastructure/Outbox/IntegrationEventReader.cs` | `Read(type, payload)` pro consumera + `ContractNames` pro topologii |
| `Infrastructure/Integration/IIntegrationEventBus.cs`, `LoggingIntegrationEventBus.cs` | jen komentáře |
| `Web/Program.cs` | `AddMessaging(builder.Configuration)` |
| `Web/appsettings.json` | sekce `Messaging` |
| `tests/.../TestDoubles/RecordingIntegrationEventBus.cs` | `FailWith` – simulace výpadku |
| `tests/.../Infrastructure/OutboxTests.cs` | 3 testy chování workera při chybě busu |

## Rozhodnutí v tomto kroku

- **RabbitMQ.Client bez nadstavby** (ne MassTransit/Wolverine). Outbox, inbox i retry už máme vlastní,
  nadstavba by je zdvojila. Navíc tak vidíte exchange, frontu, binding i ack přímo v kódu.
- **Exchange na kontrakt, typ fanout** (styl MassTransitu). Příjemce si předplatí přesně ty události, které chce,
  a nové verze (`…V2`) dostanou vlastní exchange.
- **Publisher confirms.** Bez nich by worker označil zprávu jako odeslanou, i kdyby ji broker nepřevzal.
- **Jedno spojení, vlastní kanály.** Spojení je drahé, kanál levný. Publisher a consumer mají každý svůj kanál.
- **Automatické obnovení klienta je vypnuté.** O opakování se starají worker (každá 2 s) a consumer (každých 5 s).
  Je to čitelnější než kombinovat dva mechanismy.
- **Exchange deklaruje publisher i consumer.** Deklarace je idempotentní a nezáleží na tom, kdo se připojí dřív.
  Frontu a binding deklaruje jen consumer, protože fronta patří příjemci.
- **Consumer při chybě zprávu zahodí** (nack bez vrácení do fronty). Chyba čtení (neznámý kontrakt, rozbitý JSON)
  se opakováním nespraví a vracení do fronty by vedlo k nekonečné smyčce.
- **Consumer je ve stejné aplikaci jako publisher.** Je to jen ukázka druhé strany. Skuteční příjemci (sklad,
  notifikace) přijdou v krocích 9–10.
- **Testy dál běží bez RabbitMQ.** Logika workera se testuje s `RecordingIntegrationEventBus`, bus samotný jen
  na nedostupném portu. Celá cesta přes broker byla ověřena ručně (viz výše).

## Známá omezení

- **Bez dead-letter fronty.** Zpráva, kterou consumer neumí přečíst, se zahodí, jen se zaloguje chyba.
  Správně by šla do fronty `minishop.audit.dead-letter`, kam se podívá člověk.
- **Consumer nemá inbox.** Jen loguje, takže duplicita nevadí. Skutečný příjemce v kroku 9 bude potřebovat
  inbox podle `MessageId`.
- **Při výpadku brokeru se log plní varováními** (worker každá 2 s, consumer každých 5 s). Pokus o připojení
  na Windows trvá asi 2 s a zdrží kolo workera.
- **Bez TLS, heslo guest/guest v appsettings.json** – jen pro lokální vývoj.
- Platí omezení z kroků 5–7: jedna instance workera, retry bez backoffu, inbox se nečistí.

## Jak ověřit

1. `dotnet test` – projde všech 57 testů, z toho 6 nových (3× `RabbitMqTests`, 3× `OutboxTests` s chybou busu).
2. Spusťte RabbitMQ:
   `docker run -d --name minishop-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management`
   (příště stačí `docker start minishop-rabbit`).
3. Spusťte aplikaci. V logu uvidíte `Připojeno k RabbitMQ` a `Consumer poslouchá frontu minishop.audit`.
4. Vytvořte objednávku. Do pár sekund uvidíte `[RabbitMQ] OrderPlacedV1 … publikována` a `[Přijato z minishop.audit] OrderPlacedV1 …`.
5. Otevřete `http://localhost:15672` (guest/guest):
   - **Exchanges:** `OrderPlacedV1`, `OrderCompletedV1`, `OrderCancelledV1` (typ fanout),
   - **Queues:** `minishop.audit` s jedním consumerem, na záložce *Bindings* napojení na všechny tři exchange.

## Vyzkoušejte sami

1. **Příjemce na dovolené.** V `appsettings.json` dejte `"AuditQueue": "minishop.audit.paused"`, spusťte aplikaci,
   pak ji zastavte a vraťte název zpět. Fronta `minishop.audit.paused` zůstala navázaná, ale nikdo ji nečte.
   Vytvořte objednávku. V management UI uvidíte ve frontě `minishop.audit.paused` čekající zprávu
   (*Get messages* ukáže JSON i hlavičky `message_id` a `type`). Takhle na vás broker počká.
2. **Výpadek brokeru.** Za běhu aplikace `docker stop minishop-rabbit`, vytvořte objednávku a pak `docker start minishop-rabbit`.
   Objednávka projde hned. Integrační zpráva odejde, až se broker vrátí, a v DB má `Attempts = 0`.
3. **Ruční zpráva.** V management UI otevřete exchange `OrderCompletedV1` → *Publish message*, do *Properties* dejte
   `type = OrderCompletedV1` a do *Payload* JSON s polem navíc. Consumer ho přečte (tolerant reader).
   Zkuste i `type = Nesmysl` – consumer zprávu zahodí a zaloguje chybu.
4. **Bez Dockeru.** `"Transport": "Log"` – aplikace se chová jako v kroku 7.
