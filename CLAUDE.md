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
src/MiniShop.Domain          Common/ (Entity, IDomainEvent), Orders/ (Order, CatalogProduct, Saga/OrderSaga), Inventory/ (Product, StockReservation),
                             Journeys/ (JourneyEvent, JourneyEventType)
src/MiniShop.Application     Abstractions/ (sdílené: IUnitOfWork, IDomainEvent*, IIntegrationMessage*, TraceTags),
                             Ordering/ (OrderService, DTO, Saga, Catalog projekce, EventHandlers, Pricing), Inventory/ (ProductService, Reserve/ReleaseStock),
                             Journeys/ (JourneyService, OrderJourneyProjection, JourneyReplayProjection, DTO)
src/MiniShop.Infrastructure  sdílené: Persistence/ModuleDbContext, Outbox, Inbox (InboxGuard), Integration (bus, routing, dispatcher, RabbitMq/), Events,
                             Observability/ (MessagingTelemetry – ActivitySource + Meter, OutboxHealthCheck),
                             Hosting/ (OutboxWorker, RabbitMqConsumersWorker, AddMessaging);
                             Ordering/ (OrderingDbContext, repozitáře, notifier, DB init), Inventory/ (InventoryDbContext, repozitáře, DB init + seed),
                             Journeys/ (JourneysDbContext – journeys.db bez outboxu/inboxu, repozitář, DB init)
src/MiniShop.Web             služba Ordering (7180): API objednávek, Razor Pages (celé UI), Inventory/InventoryApiClient (HTTP na sklad),
                             OrderSagaTimeoutWorker, Program.cs (composition root); krok 12: JourneysController, Pages/Orders/Journey + Replay,
                             wwwroot/js/order-form-tracker.js + journey-player.js
src/MiniShop.Inventory.Api   služba skladu (7181): ProductsController, jen modul Inventory, inventory.db, fronta minishop.inventory
src/MiniShop.ServiceDefaults AddServiceDefaults (service discovery, HTTP resilience, health checky /health, /alive, OpenTelemetry → OTLP)
src/MiniShop.AppHost         .NET Aspire: RabbitMQ (persistentní kontejner) + inventory-api + web, dashboard
tests/MiniShop.Tests         testy domény, OrderService + ságy + katalogu (2× SQLite in-memory, oba moduly v procesu), dispatcheru,
                             outboxu, inboxu, kontraktů, architektury, InventoryApiClient
docs/                        EDA-roadmap.md, kroky/ (souhrny hotových kroků), diagramy/
```

## Příkazy
- Build: `dotnet build MiniShop.slnx`
- Testy: `dotnet test MiniShop.slnx`
- Spuštění (krok 10B): `dotnet run --project src/MiniShop.AppHost` – Aspire spustí RabbitMQ, Inventory.Api (7181) a Web (7180),
  dashboard https://localhost:17180 (přihlašovací odkaz v konzoli). Potřebuje Docker.
- Bez Aspire: `docker start minishop-rabbit` (poprvé
  `docker run -d --name minishop-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4-management`; UI http://localhost:15672, guest/guest),
  pak `dotnet run --project src/MiniShop.Inventory.Api` a `dotnet run --project src/MiniShop.Web`.
  Transport `InProcess` od 10B pro spuštění nejde (dva procesy) – jen v testech.
- Při ověřování naživo pozor: zastavení AppHostu nechá běžet kontejner `messaging-…` (persistentní); procesy služeb ukončit.
- Po každé změně kódu spusť build a testy a oprav chyby, než ohlásíš hotovo.

## Konvence a záměrná rozhodnutí
- Závislosti vedou dovnitř: Web → Application → Domain, Infrastructure → Application, Application → Contracts.
- `MiniShop.Contracts` = zprávy mezi moduly/službami (DTO pro REST API jsou v modulech, např. `Application/Ordering/OrderContracts.cs`).
  Kontrakty se nepřejmenovávají ani nemění – větší změna = nová verze (`…V2`). Hlídají to `IntegrationContractTests`.
- **Modulární monolit (krok 10):** moduly Ordering (`…Domain.Orders`, `…Application.Ordering`, `…Infrastructure.Ordering`)
  a Inventory (`…Inventory` ve všech vrstvách) se navzájem **nereferencují** – hlídá `Architecture/ModuleBoundariesTests`.
  Sdílené jádro (`Domain.Common`, `Application.Abstractions`, `Infrastructure.Outbox/Inbox/Integration/Persistence/Events`) nezná žádný modul.
  Každý modul má vlastní DB (`ordering.db`, `inventory.db`), vlastní outbox + inbox a vlastní `I…UnitOfWork` / `I…MessagePublisher`
  (`IOrderingUnitOfWork`, `IInventoryMessagePublisher`, …) – handler modulu nikdy nebere obecné `IUnitOfWork`.
- **Samostatné služby (krok 10B):** Web hostuje jen Ordering (`AddInfrastructureCore` + `AddOrderingInfrastructure`),
  Inventory.Api jen Inventory. `AddInfrastructure(ordering, inventory)` (oba moduly) používají jen testy.
  Web volá sklad přes HTTP jen kvůli UI (`InventoryApiClient` : `IProductService`, `https+http://inventory-api`,
  bez Aspire adresa v `Services:inventory-api` v `appsettings.Development.json` Webu – jinde ji dává Aspire/Compose,
  `https` adresa má přednost a v kontejneru by přebila `http`); business tok jde jen přes zprávy.
  Formulář objednávky bere nabídku z kopie katalogu (`IOrderService.GetCatalogAsync`), zásobu ze skladu jen volitelně.
  Web nesmí referencovat `Domain/Infrastructure.Inventory`, Inventory.Api nic z Orderingu (testy architektury).
  Auditní frontu poslouchá jen Web (`AuditQueue` prázdná v Inventory.Api). RabbitMQ adresu dává Aspire v `ConnectionStrings:messaging`.
- Ordering zná produkty jen z kopie katalogu `CatalogProduct` (plní `CatalogProjectionHandler` z `ProductUpdatedV1`/`ProductRemovedV1`).
  Obnova kopie: příkaz `SyncCatalogV1` (`CatalogSyncService`) – automaticky po startu Webu, když je kopie prázdná
  (`CatalogSyncOnStartupWorker`), nebo tlačítkem na stránce Objednávky; sklad (`SyncCatalogHandler`) pošle `ProductUpdatedV1`
  pro každý produkt s novým `EventId`, ale s časem `Product.ChangedAtUtc` (ne „teď“ – nesmí přebít novější změnu).
  Inventory převádí doménové události produktu na integrační rovnou v `InventoryDbContext.ToOutboxMessage` (Ordering přes handler).
- `MiniShop.Application` **nemá žádné NuGet balíčky** (ani DI ani Logging) – předchází se tím konfliktu verzí
  (NU1605). Registrace aplikačních služeb a handlerů proto žije ve
  `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs`, infrastruktury v
  `src/MiniShop.Infrastructure/DependencyInjection.cs`.
- Entity mění stav jen přes metody s business pravidly; čas se předává parametrem (z `TimeProvider`),
  doména nevolá `DateTime.UtcNow`.
- Logování přes **Serilog** (jen ve službách – Web a Inventory.Api, ten do `logs/inventory-RRRRMMDD.log`): konzole + denní soubor `src/MiniShop.Web/logs/minishop-RRRRMMDD.log`
  (7 dní, v `.gitignore`). Úrovně a cíle v sekci `Serilog` v appsettings, ne v `Logging`. Ostatní vrstvy
  logují přes `ILogger<T>` a o Serilogu nevědí. `UseSerilogRequestLogging` musí být v pipeline **před** `UseExceptionHandler`
  (jinak loguje ošetřené 404/400 jako 500).
- Databáze vznikají přes `EnsureCreated` (bez migrací) v `DatabaseInitializer`. **Při změně schématu** (nová tabulka/sloupec)
  je potřeba smazat `src/MiniShop.Web/ordering.db`, `src/MiniShop.Web/journeys.db` a/nebo `src/MiniShop.Inventory.Api/inventory.db` – upozorni na to uživatele.
- Testy OrderService používají skutečný dispatcher a skutečné handlery (testují celou cestu).
  Služby skládají přes `AddInfrastructure` nad dvěma SQLite v paměti (`InMemoryDatabase` pro každý modul), každá akce
  běží ve vlastním scope a outboxy všech modulů se zpracují ručně přes `ProcessOutboxAsync()` – žádné čekání na workera.
  Produkty se v testech zakládají přes `IProductService` (sklad) a do katalogu Orderingu se dostanou až zpracováním outboxu.
- Handlery běží asynchronně (krok 5): `SaveChangesAsync` jen ukládá do outboxu, doručuje `OutboxWorker`.
- Handlery doménových událostí se registrují **jen přes `AddDomainEventHandler<TEvent, THandler>()`**
  (`Infrastructure/Ordering/OrderingInfrastructure.cs` – doménové události přes outbox má jen Ordering) – obalí je
  dekorátorem `IdempotentDomainEventHandler` (krok 6). Nikdy ne přímo `AddScoped<IDomainEventHandler<…>>`.
- Handlery integračních zpráv (`IIntegrationMessageHandler<T>`) se registrují **jen přes
  `AddIntegrationMessageHandler<TMessage, THandler>(modul)`** (krok 9); modul (`IntegrationModules.Ordering/Inventory`)
  = vlastní fronta `minishop.{modul}` v RabbitMQ. Příkaz (`IIntegrationCommand`) smí mít jen jednoho handlera (hlídá `IntegrationMessageRouting`).
  Modul musí být zaregistrovaný přes `AddIntegrationModule<TDbContext>(modul)` – určuje, ve které DB leží inbox jeho handlerů.
- **Observabilita (krok 11):** trace ID (W3C `traceparent`) se přenáší ve „obálce“, ne v kontraktu – `OutboxMessage.TraceParent`
  (plní `OutboxMessageFactory` z `Activity.Current`) a hlavička `traceparent` v RabbitMQ. Spany a metriky zpráv jen přes
  `MessagingTelemetry` (zdroj `MiniShop.Messaging`, `System.Diagnostics` – Infrastructure nemá balíčky OpenTelemetry).
  OpenTelemetry je jen v `ServiceDefaults` (`AddSource/AddMeter("MiniShop.*")`). Nový background worker, který ukládá zprávy,
  má otevřít vlastní span (jako `OrderSagaTimeoutWorker`), jinak zprávy nemají trasu. SQL spany jen s rodičem (filtr v EF instrumentaci).
  Číslo objednávky na span dává `TraceTags.TagOrder` (`Application/Abstractions`, tag `minishop.order.id`) – volá se tam, kde se pracuje s objednávkou.
  Serilog: `ClearProviders()` + `writeToProviders: true` (logy i do OTLP, na konzoli jednou).
- Objednávka: Pending → Confirmed/Rejected (rozhoduje sága), z Confirmed → Completed/Cancelled. `POST /api/orders` vrací 202.
- **Průběh objednávky (krok 12):** modul Journeys (`…Journeys` ve všech vrstvách, hostuje ho Web, vlastní `journeys.db`,
  bez zpráv → `JourneysDbContext` nedědí z `ModuleDbContext`). Proud `JourneyEvents` = jen INSERT, idempotence přes unikátní `EventId`
  z prohlížeče, řazení podle `OffsetMs` (čas od otevření formuláře). `ValidationFailed`/`OrderSubmitted` zapisuje jen server
  (stránka `Orders/Index`), Ordering o Journeys neví (hlídá `ModuleBoundariesTests`). Průběh = projekce při čtení
  (`OrderJourneyProjection`, fold). Sledování nesmí rozbít objednávku (chyba zápisu jen do logu). Ukládá se skutečně napsaný text.
  Automatické obnovení stránky Objednávky jen přes `reloadWhenIdle()` (nespustí se, když uživatel vyplňuje).
  Přehrání (12B) = druhá projekce `JourneyReplayProjection` (scénář počítá server, `journey-player.js` jen přehrává);
  `Replay.cshtml` má ruční kopii formuláře – **nové pole formuláře objednávky přidat i tam**. Nové typy událostí jen na konec
  `JourneyEventType` (ukládá se jako text). Prvky, které má přehrávač najít, mají `data-track`.

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
| 10A. Modulární monolit (2 DB, outbox/inbox na modul, kopie katalogu `CatalogProduct`, test architektury) | hotovo | `docs/kroky/krok-10a-modularni-monolit.md` |
| 10B. Vyjmutí Inventory do samostatné služby (`MiniShop.Inventory.Api`, .NET Aspire AppHost, ServiceDefaults, `InventoryApiClient`, resilience) | hotovo | `docs/kroky/krok-10b-samostatna-sluzba.md` |
| 11. Observabilita (OpenTelemetry, `TraceParent` v outboxu + hlavička `traceparent` v RabbitMQ, metriky, health check outboxu) | hotovo | `docs/kroky/krok-11-observabilita.md` |
| 12A. Průběh objednávky – proud událostí z formuláře (`JourneyEvents`, modul Journeys), projekce časová osa + souhrn (`/Orders/Journey/{id}`) | hotovo | `docs/kroky/krok-12a-prubeh-objednavky.md` |
| 12B. Přehrání průběhu objednávky (`/Orders/Replay/{id}`, `JourneyReplayProjection`, `journey-player.js`, událost `FieldInvalid`) | hotovo | `docs/kroky/krok-12b-prehrani-objednavky.md` |

Známé omezení po kroku 11: vnější akce (notifikace) nejsou v transakci – při pádu mezi odesláním a zápisem
do inboxu odejdou znovu. Inbox se nečistí. Worker počítá s jednou instancí aplikace, retry je bez backoffu.
`OrderingDomainEventReader` převádí payload zpět na událost načtením `Order` podle `OrderId` (handlery vidí aktuální stav).
Integrační zprávy jdou do RabbitMQ (`RabbitMqIntegrationBus`), nedostupný broker (`IntegrationBusUnavailableException`)
se nepočítá jako pokus. Consumery nemají dead-letter frontu: nečitelnou zprávu zahodí, při chybě handleru ji po 2 s vrátí do fronty (bez limitu).
Každý krok ságy čeká na kolo outbox workeru (2 s) → potvrzení objednávky trvá ~4–8 s, nový produkt je v nabídce po ~2–4 s.
Služby sdílí knihovny Application/Infrastructure (změna jádra = nasadit obě). Bez Dockeru se aplikace nespustí.
Obnova katalogu (`SyncCatalogV1`) produkty nemaže – zmeškané `ProductRemovedV1` neopraví.
Při výpadku skladu trvají první načtení stránek ~4 s, pak circuit breaker (ServiceDefaults) odpovídá hned.
Timeout ságy a obnova katalogu začínají vlastní trasu (nenavazují na původní požadavek). Telemetrie jde jen do Aspire
dashboardu (v paměti, bez vzorkování); bez Aspire se neexportuje, trace ID je jen v logu a v outboxu.
Testy běží bez RabbitMQ (`RecordingIntegrationBus` nad `InProcessIntegrationBus` – celá sága v procesu).
Testy: `ProcessOutboxAsync` jede kola, dokud přibývají zprávy (opakuje i selhané), `ProcessOneRoundAsync` = jedno kolo,
čas posouvá `FixedTimeProvider.Advance` (timeout ságy).

## Stav nasazení do cloudu (viz `docs/Cloud-roadmap.md`)
Cíl: Oracle Cloud Always Free (ARM VM) + Docker Compose + Caddy, CI/CD v GitHub Actions, image v GHCR, IaC v OpenTofu.
Rozpočet max. 100 Kč/měsíc. Souhrny kroků do `docs/kroky-cloud/`. Kroky v cloudové konzoli dělá uživatel sám.
- Image: `docker build -f src/MiniShop.Web/Dockerfile -t minishop-web .` (z kořene; totéž `src/MiniShop.Inventory.Api/Dockerfile`).
  Nový projekt, na kterém služba závisí, přidat do `COPY *.csproj` v obou Dockerfile.
- Konfigurace kontejneru jen proměnnými prostředí; výchozí cesty (`/data/*.db`, `/data/keys`, `/data/logs`) nastavuje
  `ENV` v Dockerfile. RabbitMQ (`ConnectionStrings__messaging`) a adresu skladu (`Services__inventory-api__http__0`) dodá spouštěč.
- Web ukládá Data Protection klíče do `DataProtection:KeysPath` (jen když je nastavené – lokálně ne).
- Git Bash: u `docker exec … /cesta` použít `MSYS_NO_PATHCONV=1`.
- Compose (krok 2): `cd deploy && docker compose up -d --build` → http://localhost:8080, RabbitMQ UI http://localhost:15673
  (údaje z `deploy/.env`, vzor `.env.example`, `.env` je v `.gitignore`). `docker compose down` data nechá, `down -v` smaže volumes.
  Nová proměnná prostředí pro službu = přidat do `compose.yaml` (a tajnou hodnotu do `.env.example` jako vzor).
  Healthcheck .NET služeb: bash `/dev/tcp` na `/health` (image nemá curl). `/health` nehlídá RabbitMQ.
- VM (krok 4): `ssh minishop-vm` (zkratka v `~/.ssh/config` uživatele, klíč s passphrase – neinteraktivně jen přes ssh-agent),
  Compose v `~/minishop` (`compose.yaml` + `.env` s `WEB_PORT=80`, `IMAGE_TAG=arm64`). Ruční nasazení: `docker buildx build
  --platform linux/arm64 … -t minishop-web:arm64 --load .` → `docker save -o` → `scp` → `docker load -i` → `docker compose up -d --no-build`.
  Z PowerShellu přes `ssh "…"` pozor na `\n` a uvozovky – skript poslat souborem.
- HTTPS (krok 5): `https://minishop-tichy.duckdns.org` (reserved IP `141.144.255.238`), Caddy z `deploy/compose.cloud.yaml`
  + `deploy/Caddyfile`, na VM zapnuté přes `COMPOSE_FILE=compose.yaml:compose.cloud.yaml` v `.env`. Web není publikovaný ven.
  Basic auth (uživatel `minishop`, heslo zná jen uživatel – změna `bash set-basic-auth.sh` na VM), vypíná `BASIC_AUTH=off`.
  Security list: 80/443 z celého internetu, 22 jen z IP uživatele. Volume `caddy-data` (certifikáty) nemazat.
- CI (krok 6): workflow „CI“, job „Build a testy“ (= povinný status check v rulesetu). Repozitář `ftichy/MiniShop` je veřejný –
  nic tajného do gitu. `main` jen přes PR se zeleným CI (bez přímého push). SDK určuje `global.json` (10.0.200, `latestFeature`).
  Nový projekt/závislost = ověřit, že CI na Linuxu projde.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Kontejnerizace a produkční konfigurace (Dockerfile multi-stage, uživatel `app`, data v `/data`, Data Protection klíče) | hotovo | `docs/kroky-cloud/krok-01-kontejnery.md` |
| 2. Docker Compose (`deploy/compose.yaml`, healthchecky, volumes, `.env`) | hotovo | `docs/kroky-cloud/krok-02-compose.md` |
| 3. Cloudový účet, rozpočet, IAM (OCI Frankfurt, compartment `minishop`, uživatel `minishop-automation`, OCI CLI z Dockeru) | hotovo (kvóta `minishop-quota`: v `minishop` jen A1 do 2 OCPU / 12 GB) | `docs/kroky-cloud/krok-03-ucet-oci.md` |
| 4. Síť a VM ručně, první nasazení (VCN, security list, VM A1 Ubuntu 24.04, Docker, image přes `docker save` + `scp`) | hotovo | `docs/kroky-cloud/krok-04-sit-a-vm.md` |
| 5. Doména, HTTPS a zámek (DuckDNS `minishop-tichy.duckdns.org`, reserved IP, Caddy + Let's Encrypt, basic auth) | hotovo | `docs/kroky-cloud/krok-05-https.md` |
| 6. CI – build a testy (`.github/workflows/ci.yml`, `global.json`, veřejný repozitář, ruleset `main-protection`) | probíhá – uživatel na GitHubu | `docs/kroky-cloud/krok-06-ci.md` |
| 7. Image do registry (GHCR) | čeká | — |
| 8. CD – automatické nasazení | čeká | — |
| 9. Infrastruktura jako kód (OpenTofu) | čeká | — |
| 10. Secrets a konfigurace | čeká | — |
| 11. Monitoring a logy v cloudu | čeká | — |
| 12. Zálohy a obnova po havárii | čeká | — |

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
