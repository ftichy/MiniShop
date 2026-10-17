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
src/MiniShop.Contracts       integrační události (OrderPlacedV1, …) – veřejné kontrakty, žádné závislosti
src/MiniShop.Domain          entity (Product, Order, OrderItem), doménové události, žádné závislosti
src/MiniShop.Application     služby, rozhraní (repozitáře, IUnitOfWork, IOrderNotifier, IDomainEvent*, IIntegrationEventPublisher), handlery událostí
src/MiniShop.Infrastructure  EF Core, repozitáře, DomainEventDispatcher, Outbox, Inbox, Integration (bus), LoggingOrderNotifier
src/MiniShop.Web             API controllery, Razor Pages, OutboxWorker, Program.cs (composition root)
tests/MiniShop.Tests         testy domény, OrderService (SQLite in-memory), dispatcheru, outboxu, inboxu
docs/                        EDA-roadmap.md, kroky/ (souhrny hotových kroků), diagramy/
```

## Příkazy
- Build: `dotnet build MiniShop.slnx`
- Testy: `dotnet test MiniShop.slnx`
- Spuštění: `dotnet run --project src/MiniShop.Web` (https://localhost:7180)
- Po každé změně kódu spusť build a testy a oprav chyby, než ohlásíš hotovo.

## Konvence a záměrná rozhodnutí
- Závislosti vedou dovnitř: Web → Application → Domain, Infrastructure → Application, Application → Contracts.
- `MiniShop.Contracts` (zprávy pro jiné služby) ≠ `MiniShop.Application/Contracts` (DTO pro REST API).
  Kontrakty se nepřejmenovávají ani nemění – větší změna = nová verze (`…V2`). Hlídají to `IntegrationEventContractTests`.
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

## Stav EDA (viz `docs/EDA-roadmap.md`)
| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Doménové události (`Entity`, `IDomainEvent`, `OrderPlaced/Completed/Cancelled`) | hotovo | — |
| 2. In-process dispatcher (`DomainEventDispatcher`, dispatch po `SaveChangesAsync`) | hotovo | `docs/kroky/krok-02-dispatcher.md` |
| 3. Handlery místo přímých volání v `OrderService` | hotovo | `docs/kroky/krok-03-handlery.md` |
| 4. Outbox pattern (`OutboxMessages` v téže transakci jako data) | hotovo | `docs/kroky/krok-04-outbox.md` |
| 5. Background worker (`OutboxWorker` → `OutboxProcessor`, jediná cesta doručení, retry max. 5×) | hotovo | `docs/kroky/krok-05-worker.md` |
| 6. Idempotentní handlery (`EventId`, inbox `ProcessedMessages`, dekorátor `IdempotentDomainEventHandler`) | hotovo | `docs/kroky/krok-06-inbox.md` |
| 7. Integrační události a kontrakty (`MiniShop.Contracts`, překladový handler, `Kind` v outboxu, `IIntegrationEventBus`) | hotovo | `docs/kroky/krok-07-integracni-udalosti.md` |
| 8. Message broker (RabbitMQ) | **další na řadě** | |
| 9.–11. | čeká | |

Známé omezení po kroku 7: vnější akce (notifikace) nejsou v transakci – při pádu mezi odesláním a zápisem
do inboxu odejdou znovu. Inbox se nečistí. Worker počítá s jednou instancí aplikace, retry je bez backoffu.
`OutboxEventReader` převádí payload zpět na událost načtením `Order` podle `OrderId` (handlery vidí aktuální stav).
Integrační události jdou zatím jen do logu (`LoggingIntegrationEventBus`); v kroku 8 se vymění jen implementace busu.
Testy: `ProcessOutboxAsync` jede kola, dokud přibývají zprávy (opakuje i selhané), `ProcessOneRoundAsync` = jedno kolo.

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
