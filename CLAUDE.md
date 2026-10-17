# MiniShop – instrukce pro Claude

## Komunikace
- S uživatelem mluv **česky** (vysvětlení, souhrny, komentáře v kódu, commit zprávy).
- Uživatel je vývojář, ale v event-driven architektuře (EDA) je **začátečník**. Vysvětluj jednoduše,
  na konkrétních místech v kódu, s přirovnáním ze života, když je to užitečné.
- Identifikátory v kódu (třídy, metody, proměnné) piš **anglicky**, komentáře a texty pro uživatele česky.

## Projekt
Výukový e-shop: ASP.NET Core (.NET 10), EF Core + SQLite, REST API + Razor Pages, xUnit.
Cíl: postupně ho převést na EDA podle plánu v `docs/EDA-roadmap.md`.

```
src/MiniShop.Contracts       integrační zprávy – události (OrderPlacedV1, StockReservedV1, …) a příkazy (ReserveStockV1, …), žádné závislosti
src/MiniShop.Domain          entity (Product, Order, OrderItem, StockReservation), doménové události, stavový automat OrderSaga, žádné závislosti
src/MiniShop.Application     služby, rozhraní (repozitáře, IUnitOfWork, IOrderNotifier, IDomainEvent*, IIntegrationMessage*), handlery,
                             Orders/Saga (OrderSagaOrchestrator), Inventory/ (modul skladu: ReserveStock/ReleaseStock)
src/MiniShop.Infrastructure  EF Core, repozitáře, DomainEventDispatcher, Outbox, Inbox (InboxGuard), Integration (bus, routing, dispatcher, RabbitMq/), LoggingOrderNotifier
src/MiniShop.Web             API controllery, Razor Pages, OutboxWorker, RabbitMqConsumersWorker, OrderSagaTimeoutWorker, Program.cs (composition root)
tests/MiniShop.Tests         testy domény (vč. OrderSaga), OrderService + ságy (SQLite in-memory), dispatcheru, outboxu, inboxu, kontraktů
docs/                        EDA-roadmap.md, kroky/ (souhrny hotových kroků), diagramy/
```

## Příkazy
- Build: `dotnet build MiniShop.slnx`
- Testy: `dotnet test MiniShop.slnx`
- Spuštění: `dotnet run --project src/MiniShop.Web` (https://localhost:7180)
- RabbitMQ (krok 8): `docker start minishop-rabbit`, poprvé
  `docker run -d --name minishop-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management`; UI http://localhost:15672 (guest/guest).
  Bez Dockeru: `Messaging:Transport` = `InProcess` v appsettings.json (zprávy se doručí handlerům přímo v procesu).
- Po každé změně kódu spusť build a testy a oprav chyby, než ohlásíš hotovo.

## Konvence a záměrná rozhodnutí
- Závislosti vedou dovnitř: Web → Application → Domain, Infrastructure → Application, Application → Contracts.
- `MiniShop.Contracts` (zprávy pro jiné služby) ≠ `MiniShop.Application/Contracts` (DTO pro REST API).
  Kontrakty se nepřejmenovávají ani nemění – větší změna = nová verze (`…V2`). Hlídají to `IntegrationContractTests`.
- `MiniShop.Application` **nemá žádné NuGet balíčky** (ani DI ani Logging) – předchází se tím konfliktu verzí
  (NU1605). Registrace aplikačních služeb a handlerů proto žije ve
  `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs`, infrastruktury v
  `src/MiniShop.Infrastructure/DependencyInjection.cs`.
- Entity mění stav jen přes metody s business pravidly; čas se předává parametrem (z `TimeProvider`),
  doména nevolá `DateTime.UtcNow`.
- Logování přes **Serilog** (jen v `MiniShop.Web`): konzole + denní soubor `src/MiniShop.Web/logs/minishop-RRRRMMDD.log`
  (7 dní, v `.gitignore`). Úrovně a cíle v sekci `Serilog` v appsettings, ne v `Logging`. Ostatní vrstvy
  logují přes `ILogger<T>` a o Serilogu nevědí.
- Databáze vzniká přes `EnsureCreated` (bez migrací). **Při změně schématu** (nová tabulka/sloupec)
  je potřeba smazat `src/MiniShop.Web/minishop.db` – upozorni na to uživatele.
- Testy OrderService používají skutečný dispatcher a skutečné handlery (testují celou cestu).
  Služby skládají přes `AddInfrastructure` nad sdílenou SQLite v paměti (`InMemoryDatabase`), každá akce
  běží ve vlastním scope a outbox se zpracuje ručně přes `ProcessOutboxAsync()` – žádné čekání na workera.
- Handlery běží asynchronně (krok 5): `SaveChangesAsync` jen ukládá do outboxu, doručuje `OutboxWorker`.
- Handlery doménových událostí se registrují **jen přes `AddDomainEventHandler<TEvent, THandler>()`**
  (Infrastructure) – obalí je dekorátorem `IdempotentDomainEventHandler` (krok 6). Nikdy ne přímo `AddScoped<IDomainEventHandler<…>>`.
- Handlery integračních zpráv (`IIntegrationMessageHandler<T>`) se registrují **jen přes
  `AddIntegrationMessageHandler<TMessage, THandler>(modul)`** (krok 9); modul (`IntegrationModules.Ordering/Inventory`)
  = vlastní fronta `minishop.{modul}` v RabbitMQ. Příkaz (`IIntegrationCommand`) smí mít jen jednoho handlera (hlídá `IntegrationMessageRouting`).
- Modul Inventory nesahá na `Orders` a Ordering nesahá na sklad – komunikují jen kontrakty (příprava na krok 10).
- Objednávka: Pending → Confirmed/Rejected (rozhoduje sága), z Confirmed → Completed/Cancelled. `POST /api/orders` vrací 202.

## Stav EDA (viz `docs/EDA-roadmap.md`)
| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Doménové události (`Entity`, `IDomainEvent`, `OrderPlaced/Completed/Cancelled`) | hotovo | — |
| 2. In-process dispatcher (`DomainEventDispatcher`, dispatch po `SaveChangesAsync`) | hotovo | `docs/kroky/krok-02-dispatcher.md` |
| 3. Handlery místo přímých volání v `OrderService` | hotovo | `docs/kroky/krok-03-handlery.md` |
| 4. Outbox pattern (`OutboxMessages` v téže transakci jako data) | hotovo | `docs/kroky/krok-04-outbox.md` |
| 5. Background worker (`OutboxWorker` → `OutboxProcessor`, jediná cesta doručení, retry max. 5×) | hotovo | `docs/kroky/krok-05-worker.md` |
| 6. Idempotentní handlery (`EventId`, inbox `ProcessedMessages`, dekorátor `IdempotentDomainEventHandler`) | hotovo | `docs/kroky/krok-06-inbox.md` |
| 7. Integrační události a kontrakty (`MiniShop.Contracts`, překladový handler, `Kind` v outboxu, `IIntegrationBus`) | hotovo | `docs/kroky/krok-07-integracni-udalosti.md` |
| 8. Message broker (RabbitMQ.Client, exchange fanout na kontrakt, publisher confirms, ukázkový consumer `minishop.audit`) | hotovo | `docs/kroky/krok-08-rabbitmq.md` |
| 9. Sága objednávky – orchestrace (`OrderSaga` + `OrderSagaOrchestrator`, příkazy `ReserveStockV1`/`ReleaseStockV1`, modul Inventory, timeout, kompenzace) | hotovo | `docs/kroky/krok-09-saga.md` |
| 10. Rozdělení na samostatné služby | **další na řadě** | |
| 11. Observabilita | čeká | |

Známé omezení po kroku 9: vnější akce (notifikace) nejsou v transakci – při pádu mezi odesláním a zápisem
do inboxu odejdou znovu. Inbox se nečistí. Worker počítá s jednou instancí aplikace, retry je bez backoffu.
`OutboxEventReader` převádí payload zpět na událost načtením `Order` podle `OrderId` (handlery vidí aktuální stav).
Integrační zprávy jdou do RabbitMQ (`RabbitMqIntegrationBus`), nedostupný broker (`IntegrationBusUnavailableException`)
se nepočítá jako pokus. Consumery nemají dead-letter frontu: nečitelnou zprávu zahodí, při chybě handleru ji po 2 s vrátí do fronty (bez limitu).
Každý krok ságy čeká na kolo outbox workeru (2 s) → potvrzení objednávky trvá ~4–8 s. Katalog (ceny) a sklad
sdílejí tabulku `Products` (rozdělí krok 10).
Testy běží bez RabbitMQ (`RecordingIntegrationBus` nad `InProcessIntegrationBus` – celá sága v procesu).
Testy: `ProcessOutboxAsync` jede kola, dokud přibývají zprávy (opakuje i selhané), `ProcessOneRoundAsync` = jedno kolo,
čas posouvá `FixedTimeProvider.Advance` (timeout ságy).

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
