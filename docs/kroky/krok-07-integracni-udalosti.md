# Krok 7: Integrační události a kontrakty

Vedle interních doménových událostí teď MiniShop publikuje **integrační události** (`OrderPlacedV1`,
`OrderCompletedV1`, `OrderCancelledV1`), které jsou určené pro jiné služby. Jejich tvar je **kontrakt**
a žije v novém projektu `MiniShop.Contracts`. Brokera ještě nemáme, takže zatím „odcházejí ven“ jen do logu.

## Jak to funguje

Přirovnání: doménová událost je **interní poznámka v kanceláři**, kterou můžete kdykoli přepsat.
Integrační událost je **formulář, který posíláte na úřad**. Jeho kolonky se nemění. Když je potřeba nová,
vydá se „formulář V2“ a chvíli platí oba.

|  | Doménová (`OrderPlaced`) | Integrační (`OrderPlacedV1`) |
| --- | --- | --- |
| Kdo ji čte | handlery v MiniShopu | jiné služby (od kroku 8 přes RabbitMQ) |
| Obsah | celá entita `Order` | jen data: ID, e-mail, cena, položky |
| Změna tvaru | kdykoli | jen zpětně kompatibilně, jinak nová verze |
| Kde žije | `MiniShop.Domain` | `MiniShop.Contracts` (bez závislostí) |

**Překladač** je obyčejný handler doménových událostí, `PublishOrderIntegrationEventsHandler`. Z `OrderPlaced`
vyrobí `OrderPlacedV1` a zavolá `IIntegrationEventPublisher.Publish`. Publikování znamená **zápis do outboxu**.
Zpráva má nový sloupec `Kind = Integration`, takže ji worker nepředá handlerům, ale pošle ven přes `IIntegrationEventBus`:

```
OrderService ─► SaveChanges ─► outbox: OrderPlaced     (Kind = Domain)
worker kolo 1 ─► dispatcher ─► handlery: potvrzení, log, …
                            └► PublishOrderIntegrationEventsHandler ─► outbox: OrderPlacedV1 (Kind = Integration)
worker kolo 2 ─► IIntegrationEventBus.PublishAsync(OrderPlacedV1)   ← dnes log, v kroku 8 RabbitMQ
```

Překladový handler běží uvnitř dekorátoru z kroku 6. Zápis `OrderPlacedV1` do outboxu se proto potvrdí
**ve stejné transakci jako odškrtnutí v inboxu**. Když se doménová událost doručí znovu (např. protože selhala
notifikace), integrační událost už podruhé nevznikne.

Výsledek v logu:

```
[INF] [Událost] OrderPlaced: objednávka 1, 1 položek, celkem 1780.0 Kč
[INF] [Integrační událost] OrderPlacedV1 23fc6f10-… odeslána ven: {"EventId":"23fc6f10-…","OccurredAtUtc":"…","OrderId":1,
      "CustomerEmail":"jan@example.com","TotalPrice":1780.0,"Lines":[{"ProductId":2,"Quantity":2}]}
```

**Pravidla verzování** (jsou i v komentáři u `IIntegrationEvent`):

- Nové **volitelné** pole přidat smíte.
- Přejmenovat nebo odebrat pole nesmíte. Vytvoří se `…V2` a chvíli se publikují obě verze.
- Příjemce ignoruje pole, která nezná (**tolerant reader**). `System.Text.Json` to dělá automaticky.

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `src/MiniShop.Contracts/MiniShop.Contracts.csproj` | nový projekt bez závislostí |
| `Contracts/IIntegrationEvent.cs` | `EventId`, `OccurredAtUtc` + pravidla verzování v komentáři |
| `Contracts/Orders/OrderPlacedV1.cs`, `OrderCompletedV1.cs`, `OrderCancelledV1.cs`, `OrderLineV1.cs` | kontrakty |
| `Application/Abstractions/IIntegrationEventPublisher.cs` | „publikuj integrační událost“ (zapíše se do outboxu) |
| `Application/Orders/IntegrationEvents/PublishOrderIntegrationEventsHandler.cs` | překlad doménová → integrační |
| `Infrastructure/Integration/OutboxIntegrationEventPublisher.cs` | implementace publisheru: řádek do outboxu |
| `Infrastructure/Integration/IIntegrationEventBus.cs` | odeslání ven (volá jen worker) |
| `Infrastructure/Integration/LoggingIntegrationEventBus.cs` | zástupce brokeru: zaloguje JSON |
| `Infrastructure/Outbox/IntegrationEventReader.cs` | z řádku outboxu znovu sestaví kontrakt podle názvu typu |
| `tests/.../Contracts/IntegrationEventContractTests.cs` | 6 testů kontraktů |
| `tests/.../TestDoubles/RecordingIntegrationEventBus.cs` | testovací „broker“ |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `MiniShop.slnx` | nový projekt `MiniShop.Contracts` |
| `Application/MiniShop.Application.csproj` | reference na `MiniShop.Contracts` |
| `Infrastructure/Outbox/OutboxMessage.cs` | sloupec `Kind` + enum `OutboxMessageKind` (`Domain` / `Integration`) |
| `Infrastructure/Outbox/OutboxMessageFactory.cs` | nové přetížení `Create(IIntegrationEvent)`, payload = JSON kontraktu |
| `Infrastructure/Outbox/OutboxProcessor.cs` | podle `Kind` doručí handlerům, nebo pošle ven přes bus |
| `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` | `Kind` se ukládá jako text |
| `Infrastructure/DependencyInjection.cs` | registrace publisheru a busu |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | registrace překladového handleru pro 3 události |
| `Domain/Orders/Events/OrderPlaced.cs` | jen komentář |
| `tests/.../Application/OrderServiceTests.cs` | registrace překladače a busu; `ProcessOutboxAsync` jede, dokud přibývají zprávy; nová `ProcessOneRoundAsync`; 3 nové testy |

## Rozhodnutí v tomto kroku

- **Doménové události zůstaly beze změny.** Integrační jsou další vrstva vedle nich, ne jejich náhrada.
  Uvnitř aplikace si dál můžeme dovolit pohodlí celé entity.
- **Překlad v handleru, ne v `SaveChangesAsync`.** Handler je na jednom místě, v aplikační vrstvě, a díky inboxu
  zapíše integrační událost právě jednou. Cena za to: zpráva jde ven o jedno kolo workera (cca 2 s) později.
- **Jeden outbox, dva druhy zpráv** (sloupec `Kind`). Nová tabulka by znamenala druhý worker se stejnou logikou
  retry a poison messages.
- **Integrační událost má vlastní `EventId`**, jiné než doménová. Je to jiná zpráva pro jiné příjemce
  a její `EventId` je zároveň `Id` řádku v outboxu.
- **`OccurredAtUtc` se přebírá z doménové události.** Říká, kdy se fakt stal, ne kdy jsme zprávu odeslali.
- **Kontrakty obsahují jen to, co příjemci podle roadmapy potřebují.** Sklad potřebuje položky (`Lines`),
  notifikace e-mail. Co v kontraktu není, to se nedá rozbít. Přidat pole později je snadné, odebrat ho ne.
- **`IIntegrationEventBus` je jen v Infrastructure.** Aplikace o odesílání ven neví, zná jen `IIntegrationEventPublisher`.
  V kroku 8 se vymění jen implementace busu.
- **Typ zprávy = název třídy** (`OrderPlacedV1`). Kontrakt se nikdy nepřejmenovává, takže je to bezpečné.
  Test `VsechnyKontrakty_MajiVerziVNazvu` hlídá, že každý kontrakt má verzi v názvu.
- **Pozor na podobná jména:** `MiniShop.Application/Contracts` jsou DTO pro REST API (request/response),
  `MiniShop.Contracts` jsou zprávy pro jiné služby.

## Známá omezení

- **Překladač vidí aktuální stav objednávky**, ne stav v okamžiku události (viz `OutboxEventReader`). Položky,
  cena a e-mail se po vytvoření nemění, takže to zatím nevadí.
- **Ven zatím nic neodchází**, jen do logu. Opravdové odeslání přinese krok 8.
- **Kontraktové testy hlídají jen `OrderPlacedV1` a `OrderCancelledV1`.** `OrderCompletedV1` hlídá jen test „tam a zpět“.
- Platí omezení z kroků 5 a 6: jedna instance workera, retry bez backoffu, inbox se nečistí.

⚠️ Přibyl sloupec `Kind` v `OutboxMessages` – před spuštěním smažte `src/MiniShop.Web/minishop.db`.

## Jak ověřit

1. `dotnet test` – projde všech 51 testů, z toho 9 nových (6× `IntegrationEventContractTests`,
   `PlaceOrder_WorkerPublikujeOrderPlacedV1SDatyObjednavky`, `PlaceOrder_IntegracniUdalostJeVOutboxuJakoSamostatnaZprava`,
   `CancelAComplete_WorkerPublikujeIntegracniUdalosti`). Test `Cancel_NotifikacePoprveSelze_…` nově ověřuje
   i to, že `OrderCancelledV1` odešel jen jednou.
2. Smažte `minishop.db`, spusťte aplikaci, vytvořte objednávku a zrušte ji.
3. V logu uvidíte `[Integrační událost] OrderPlacedV1 … odeslána ven: {JSON}` a totéž pro `OrderCancelledV1`.
4. V DB Browseru for SQLite má `OutboxMessages` čtyři řádky: `OrderPlaced` a `OrderCancelled` s `Kind = Domain`,
   `OrderPlacedV1` a `OrderCancelledV1` s `Kind = Integration`. Všechny mají vyplněné `ProcessedAtUtc`.

## Vyzkoušejte sami

1. **Rozbijte kontrakt.** V `OrderPlacedV1` přejmenujte `CustomerEmail` na `Email` a spusťte testy.
   `OrderPlacedV1_TvarJsonuSeNezmenil` spadne, protože jiná služba by e-mail už nenašla. Vraťte změnu zpět.
2. **Přidejte verzi 2.** Vytvořte `OrderPlacedV2` s novým polem (např. `string CustomerName`) a v překladači
   publikujte obě verze: `publisher.Publish(v1); publisher.Publish(v2);`. V logu uvidíte dvě zprávy.
   Tak to vypadá v praxi, dokud všichni příjemci nepřejdou na V2.
3. **Tolerant reader.** V testu `Reader_NeznamaPoleIgnoruje` je v JSONu pole `CompletedBy`, které `OrderCompletedV1`
   nezná, a zpráva se přesto načte. Zkuste přidat další neznámá pole.
