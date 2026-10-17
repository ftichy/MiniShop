# Krok 9: Sága objednávky – rezervace skladu a kompenzace (orchestrace)

Objednávka už nesahá přímo na sklad. Vznikne jako **Pending** a o tom, jestli bude **Confirmed**, nebo **Rejected**,
rozhoduje **sága**: řada malých lokálních transakcí propojených zprávami. Řídí ji **orchestrátor**
(`OrderSagaOrchestrator`), který posílá skladu **příkazy** a čeká na jeho **odpovědi**. Když něco nevyjde, místo
rollbacku přijde **kompenzace**, tedy business opačný krok („uvolni rezervaci“).

## Jak to funguje

Přirovnání: číšník (orchestrátor) zapíše objednávku do bloku jako „čeká“ a pošle do kuchyně lístek „připravte“
(příkaz). Kuchyně (Inventory) odpoví „uděláme“, nebo „došly suroviny“ (události). Číšník podle toho hostovi
objednávku potvrdí, nebo se omluví. Když kuchyně neodpoví do 30 s, omluví se taky. Kdyby pak kuchyně jídlo
přece jen udělala, číšník jí řekne „vraťte to“ (kompenzace). V bloku má číšník vždy přehled, kde která objednávka je.

```
POST /api/orders ─► Order (Pending) ─► OrderPlaced ─► [Orchestrátor] založí ságu ─► ReserveStockV1 ─► [Inventory]
                                                                                                 │
     ┌──────── StockReservedV1 ◄──── všechno skladem: odečte, zapíše StockReservations ◄────────┤
     │         StockReservationFailedV1 ◄──── něco chybí: nic nemění ◄──────────────────────────┘
     ▼
[Orchestrátor] ─► Order.Confirm()  → OrderConfirmed → potvrzení zákazníkovi
              └─► Order.Reject()   → OrderRejected  → omluva s důvodem

Zrušení potvrzené: OrderCancelled ─► [Orchestrátor] ─► ReleaseStockV1 ─► [Inventory] vrátí zboží ─► StockReleasedV1
Timeout (30 s):    OrderSagaTimeoutWorker ─► [Orchestrátor] ─► Order.Reject("Sklad neodpověděl včas.")
                   …a když rezervace přijde pozdě ─► ReleaseStockV1 (kompenzace)
```

**Stavový automat ságy** (`Domain/Orders/Saga/OrderSaga.cs`, tabulka `OrderSagas`):

```
AwaitingReservation ──StockReserved──────► Reserved ──OrderCompleted──► Completed
       │                                      │
       ├─StockReservationFailed─┐             └─OrderCancelled──► ReleasingStock ──StockReleased──► Released
       └─timeout────────────────┴► Rejected ──StockReserved (pozdě!)──┘
```

Každá metoda automatu odpovídá jedné zprávě, která může přijít, a vrací akci pro orchestrátor
(`ConfirmOrder`, `RejectOrder`, `ReleaseStock`, `None`). Zpráva, která do stavu nepatří (duplicita, zpoždění),
vrátí `None` a nic se nestane. Druhou pojistkou proti duplicitě je inbox.

**Příkaz vs. událost.** Obojí jsou kontrakty v `MiniShop.Contracts` a putují stejně (outbox → bus → fronta).

|  | Příkaz (`IIntegrationCommand`) | Událost (`IIntegrationEvent`) |
| --- | --- | --- |
| Význam | „udělej“ – `ReserveStockV1` | „stalo se“ – `StockReservedV1` |
| Příjemců | právě jeden (služba, která ho vlastní) | 0 až N |
| ID | `CommandId` | `EventId` |
| Hlídá | `IntegrationMessageRouting` – druhý handler příkazu = chyba při startu | — |

Společný předek je `IIntegrationMessage` s `MessageId`. Ten se do JSONu nedostane, takže kontrakty z kroku 7
zůstaly beze změny.

**Moduly a fronty.** Handlery integračních zpráv patří do modulu a každý modul má v RabbitMQ vlastní frontu:

| Fronta | Odebírá | Handlery |
| --- | --- | --- |
| `minishop.inventory` | `ReserveStockV1`, `ReleaseStockV1` | `ReserveStockHandler`, `ReleaseStockHandler` |
| `minishop.ordering` | `StockReservedV1`, `StockReservationFailedV1`, `StockReleasedV1` | `OrderSagaOrchestrator` |
| `minishop.audit` | všechno | jen log („odposlech“) |

Bez Dockeru (`Messaging:Transport = InProcess`) doručí `InProcessIntegrationBus` zprávy handlerům modulů přímo
v procesu. Sága tak funguje stejně, jen bez brokeru.

Ověřeno naostro s RabbitMQ (timeout zkrácený na 10 s):

```
[19:29:09] [Událost] OrderPlaced: objednávka 1 …   /  objednávka 2 (999 monitorů)
[19:29:11] [RabbitMQ] ReserveStockV1 … publikována     (×2)
[19:29:13] [RabbitMQ] StockReservedV1 …                 /  StockReservationFailedV1 …
[19:29:15] Objednávka 1 potvrzena pro jan@example.com   →  objednávka 2: Rejected „Monitor 27": požadováno 999 ks, skladem 5 ks“
--- zrušení objednávky 1 ---
[19:29:31] [RabbitMQ] ReleaseStockV1 …  →  [19:29:33] StockReleasedV1 …  →  myš zpět na 40 ks
--- docker stop, objednávka 3 ---
[19:30:05] [Sága] Objednávka 3: sklad neodpověděl včas, objednávka zamítnuta
--- docker start ---
[19:30:19] ReserveStockV1 → [19:30:21] StockReservedV1 (pozdě!) → [19:30:23] ReleaseStockV1 → [19:30:25] StockReleasedV1
           → klávesnice zpět na 15 ks, objednávka 3 zůstala Rejected
```

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `Contracts/IIntegrationMessage.cs`, `IIntegrationCommand.cs` | společný předek zpráv, příkaz (`CommandId`) |
| `Contracts/Inventory/ReserveStockV1.cs`, `ReleaseStockV1.cs`, `StockLineV1.cs` | příkazy pro sklad |
| `Contracts/Inventory/StockReservedV1.cs`, `StockReservationFailedV1.cs`, `StockReleasedV1.cs` | odpovědi skladu |
| `Domain/Orders/Saga/OrderSaga.cs` | stavový automat ságy (+ `OrderSagaState`, `OrderSagaAction`) |
| `Domain/Orders/Events/OrderConfirmed.cs`, `OrderRejected.cs` | nové doménové události |
| `Domain/Inventory/StockReservation.cs` | rezervace (kolik čeho pro kterou objednávku) |
| `Application/IntegrationModules.cs` | názvy modulů `ordering`, `inventory` |
| `Application/Abstractions/IIntegrationMessageHandler.cs` | handler integrační zprávy |
| `Application/Abstractions/IOrderSagaRepository.cs`, `IStockReservationRepository.cs` | repozitáře |
| `Application/Orders/Saga/OrderSagaOrchestrator.cs` | orchestrátor + `OrderSagaSettings` (timeout) |
| `Application/Orders/EventHandlers/SendRejectionNoticeHandler.cs` | omluva zákazníkovi |
| `Application/Inventory/ReserveStockHandler.cs`, `ReleaseStockHandler.cs` | modul Inventory |
| `Infrastructure/Inbox/InboxGuard.cs` | inbox vytažený z dekorátoru – sdílí ho doménové i integrační handlery |
| `Infrastructure/Integration/IntegrationMessageRouting.cs` | kdo co odebírá, pravidlo „příkaz = jeden příjemce“ |
| `Infrastructure/Integration/IntegrationMessageDispatcher.cs` | doručí zprávu handlerům jednoho modulu přes inbox |
| `Infrastructure/Integration/RabbitMq/RabbitMqQueueConsumer.cs` | společný základ consumerů (fronta, binding, ack, retry) |
| `Infrastructure/Integration/RabbitMq/RabbitMqModuleConsumer.cs` | consumer modulu + `RabbitMqConsumers` (vyrobí všechny) |
| `Infrastructure/Repositories/OrderSagaRepository.cs`, `StockReservationRepository.cs` | repozitáře |
| `Infrastructure/Persistence/Configurations/OrderSagaConfiguration.cs`, `StockReservationConfiguration.cs` | tabulky `OrderSagas`, `StockReservations` |
| `Web/BackgroundServices/OrderSagaTimeoutWorker.cs` | každých 5 s hledá ságy po termínu |
| `tests/.../Domain/OrderSagaTests.cs` | 12 testů stavového automatu |
| `tests/.../Infrastructure/IntegrationMessageRoutingTests.cs` | 3 testy směrování |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs`, `OrderStatus.cs` | stavy Pending/Confirmed/Rejected/Completed/Cancelled, `Confirm`, `Reject(reason)`, `RejectionReason`; zrušit/vyřídit jen potvrzenou |
| `Contracts/IIntegrationEvent.cs` | dědí z `IIntegrationMessage` |
| `Application/Services/OrderService.cs` | už neodečítá sklad |
| `Application/Abstractions/IOrderNotifier.cs` | `OrderConfirmedAsync`, `OrderRejectedAsync` místo `OrderPlacedAsync` |
| `Application/Abstractions/IIntegrationMessagePublisher.cs` | přejmenováno z `IIntegrationEventPublisher`, přibyl `Send(command)` |
| `Application/Orders/EventHandlers/SendOrderConfirmationHandler.cs` | reaguje na `OrderConfirmed` |
| `Application/Contracts/OrderContracts.cs`, `Mapping/MappingExtensions.cs` | `OrderDto.RejectionReason` |
| `Infrastructure/Integration/IIntegrationBus.cs` | přejmenováno z `IIntegrationEventBus`, bere `IIntegrationMessage` |
| `Infrastructure/Integration/InProcessIntegrationBus.cs` | nahradil `LoggingIntegrationEventBus` – doručuje handlerům modulů |
| `Infrastructure/Integration/RabbitMq/RabbitMqIntegrationBus.cs`, `RabbitMqMessage.cs` | přejmenováno, zprávy místo událostí |
| `Infrastructure/Integration/RabbitMq/RabbitMqAuditConsumer.cs` | potomek `RabbitMqQueueConsumer` |
| `Infrastructure/Integration/OutboxIntegrationMessagePublisher.cs` | přejmenováno, `Send` |
| `Infrastructure/Outbox/IntegrationMessageReader.cs` | přejmenováno z `IntegrationEventReader` |
| `Infrastructure/Outbox/OutboxMessageFactory.cs`, `OutboxEventReader.cs`, `OutboxProcessor.cs` | nové události, zprávy místo událostí |
| `Infrastructure/Inbox/IdempotentDomainEventHandler.cs` | používá `InboxGuard` |
| `Infrastructure/Persistence/MiniShopDbContext.cs`, `OrderConfiguration.cs` | `OrderSagas`, `StockReservations`, `RejectionReason` |
| `Infrastructure/DependencyInjection.cs` | `AddIntegrationMessageHandler<TMessage, THandler>(modul)`, routing, dispatcher, repozitáře |
| `Infrastructure/Events/Handlers/OrderEventsLogHandler.cs`, `Notifications/LoggingOrderNotifier.cs` | nové události |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | `AddOrderSaga`, `AddInventoryModule`; `AddApplication(configuration)` |
| `Web/Extensions/MessagingServiceCollectionExtensions.cs` | transport `InProcess` místo `Log` |
| `Web/BackgroundServices/RabbitMqConsumersWorker.cs` | přejmenováno, spouští všechny consumery |
| `Web/Controllers/OrdersController.cs` | `POST` vrací **202 Accepted** |
| `Web/Pages/Orders/Index.cshtml(.cs)` | barevné stavy, důvod zamítnutí, tlačítka jen u potvrzené, automatické obnovení při Pending |
| `Web/Program.cs`, `appsettings.json` | `OrderSagaTimeoutWorker`, sekce `Sagas` |
| smazáno `Application/Orders/EventHandlers/ReturnStockOnOrderCancelledHandler.cs` | vrácení zboží převzal sklad (`ReleaseStockHandler`) |
| testy | `OrderServiceTests` přepsané na ságu; `FixedTimeProvider.Advance`; `RecordingIntegrationBus` umí předat zprávu dál; doménové testy podle nových stavů |

## Rozhodnutí v tomto kroku

- **Orchestrace, ne choreografie.** Celý průběh je na jednom místě, v tabulce `OrderSagas` je vidět, kde
  objednávka „visí“, a jde přidat timeout. Cena: víc kódu (příkazy, stav ságy, timeout worker).
- **Stavový automat v doméně, provedení v aplikaci.** `OrderSaga` jen rozhoduje (a jde testovat bez DB),
  `OrderSagaOrchestrator` provede akci (pošle příkaz, potvrdí objednávku) a uloží vše v jedné transakci.
- **Orchestrátor poslouchá doménové události objednávky** (`OrderPlaced`, `OrderCancelled`, `OrderCompleted`),
  protože je ve stejném modulu jako objednávky. Odpovědi skladu přicházejí jako integrační zprávy přes frontu.
- **Rezervace hned odečte `StockQuantity`.** Rozdíl „rezervováno“ vs. „vyskladněno“ pro výuku nepotřebujeme.
  Rezervuje se **všechno, nebo nic**.
- **Čekající objednávku nejde zrušit ani vyřídit.** Ověření trvá pár sekund a odpadne tím závod „zrušení vs. rezervace“.
- **Timeout 30 s + kompenzace pozdní rezervace.** Bez timeoutu by objednávka při výpadku skladu visela navždy.
  Pozdní `StockReservedV1` už objednávku nepotvrdí, jen spustí `ReleaseStockV1`.
- **Concurrency token `Version` na ságe.** Timeout worker a odpověď skladu můžou ságu měnit souběžně. Druhý
  zápis pak selže a zpráva se zpracuje znovu nad čerstvým stavem.
- **Příkazy i události mají v RabbitMQ exchange podle kontraktu.** Rozdíl je jen v tom, kdo se naváže. Odesílatel
  příkazu tak nemusí znát frontu příjemce.
- **`InProcess` místo `Log`.** Bez brokeru by sága stála. `InProcessIntegrationBus` doručí zprávy handlerům v procesu
  a stejně se testuje celá sága.
- **Inbox vytažen do `InboxGuard`.** Stejná logika pro doménové handlery (dekorátor) i integrační (dispatcher).
- **Přejmenování** `…Event…` → `…Message…` u busu, publisheru a readeru, protože teď nesou i příkazy. Kontrakty
  se nezměnily, testy tvaru JSONu prošly beze změny.

## Známá omezení

- **Pomalejší potvrzení:** každý krok ságy čeká na kolo outbox workeru (2 s). Pending → Confirmed trvá asi 4–8 s.
- **Consumer opakuje chybu handleru bez limitu** (po 2 s vrátí zprávu do fronty). Dead-letter fronta pořád chybí.
- **Ságu neexistující objednávky** (např. objednávky z doby před krokem 9) orchestrátor neumí. Odpověď skončí chybou
  a opakuje se. Proto je potřeba smazat DB.
- **Katalog a sklad sdílejí tabulku `Products`:** `OrderService` z ní čte cenu, Inventory zásobu. Rozdělí to krok 10.
- **Integrační události `OrderConfirmedV1` / `OrderRejectedV1` zatím nejsou.** Notifikace běží v procesu přes doménové
  události. Přidají se, až budou notifikace samostatná služba.
- Platí omezení z kroků 5–8: jedna instance workera, retry outboxu bez backoffu, inbox se nečistí.

⚠️ Změnilo se schéma (stavy objednávek, `RejectionReason`, tabulky `OrderSagas` a `StockReservations`) –
před spuštěním smažte `src/MiniShop.Web/minishop.db`.

## Jak ověřit

1. `dotnet test` – projde všech 87 testů. Nové jsou mimo jiné:
   - `OrderSagaTests` (12×),
   - `IntegrationMessageRoutingTests` (3×),
   - kontraktové testy příkazů (4×),
   - `OrderServiceTests`: `PlaceOrder_NedostatekZbozi_ObjednavkaZamitnutaSDuvodem`, `PlaceOrder_VicePolozek_RezervujeVsechnoNeboNic`,
     `Cancel_PotvrzenouObjednavku_SklademSeVratiPresKompenzaci`, `Timeout_SkladNeodpovedel_ObjednavkaZamitnutaAPozdniRezervaceSeUvolni`,
     `Saga_DuplicitniOdpovedSkladu_SeIgnoruje`.
2. Smažte `minishop.db`, spusťte RabbitMQ (`docker start minishop-rabbit`) a aplikaci.
3. Na stránce Objednávky objednejte 1 myš. Objeví se žlutý stav „Ověřujeme sklad…“ a stránka se sama obnovuje.
   Do pár sekund se změní na modrý „Potvrzená“.
4. Objednejte 999 monitorů. Žádná chyba se neobjeví, objednávka skončí jako červená „Zamítnutá“ s důvodem.
5. Zrušte potvrzenou objednávku. Za pár sekund se zboží vrátí na sklad (stránka Produkty).
6. V management UI (`http://localhost:15672`) uvidíte fronty `minishop.inventory`, `minishop.ordering` a `minishop.audit`
   a exchange pro všechny kontrakty včetně `ReserveStockV1`.
7. V DB Browseru for SQLite: tabulka `OrderSagas` (stav každé ságy) a `StockReservations` (kdo co rezervoval, `ReleasedAtUtc`).

## Vyzkoušejte sami

1. **Timeout a kompenzace.** V `appsettings.json` dejte `"ReservationTimeoutSeconds": 10`. Za běhu aplikace
   `docker stop minishop-rabbit`, vytvořte objednávku a počkejte 15 s. Objednávka bude zamítnutá
   („Sklad neodpověděl včas.“). Pak `docker start minishop-rabbit` a sledujte log: sklad zboží pozdě zarezervuje
   a sága ho hned pošle zpátky (`ReleaseStockV1`). V `OrderSagas` skončí stav `Released`.
2. **Kde objednávka visí?** Zastavte broker a vytvořte objednávku. V `OrderSagas` je `AwaitingReservation`
   s termínem `ReservationDeadlineUtc`. Tohle je hlavní výhoda orchestrace.
3. **Pravidlo příkazů.** Zkuste v `AddInventoryModule` zaregistrovat `ReserveStockV1` podruhé s jiným handlerem.
   Aplikace nenastartuje, protože příkaz smí mít jen jednoho příjemce.
4. **Bez Dockeru.** `"Transport": "InProcess"` – sága funguje stejně, jen bez front.
5. **Choreografie pro srovnání.** Rozmyslete si, jak by to vypadalo bez orchestrátoru: Inventory by odebíral
   `OrderPlacedV1` a Ordering `StockReservedV1`. Kde by pak byl timeout a kde byste hledali, kde objednávka visí?
