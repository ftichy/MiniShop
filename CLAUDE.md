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
  **Výjimka: `users.db` (Accounts) má od účtů kroku 6 EF Core migrace** – nikdy nemazat; změna schématu = nová migrace
  (`dotnet tool restore`, pak `dotnet ef migrations add <Nazev> --project src/MiniShop.Infrastructure --context AccountsDbContext --output-dir Accounts/Migrations`).
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

## Dokumentace v aplikaci (`/Docs`, menu Dokumentace)
- Zdroj: Markdown + Mermaid v `docs/prirucka/` (hlavička `title`/`summary`/`order`), Web je vykreslí za běhu
  (`src/MiniShop.Web/Documentation/`, Markdig; Mermaid lokálně ve `wwwroot/lib/mermaid`). Soubory se kopírují do buildu
  i do image (`COPY docs/prirucka` v Dockerfile Webu, výjimka v `.dockerignore`); v Development se čtou přímo z repozitáře.
- Odkazy: na stránku relativně `[x](../eda/inbox.md)`, do kódu `[x](code:src/…)`, v diagramu `click uzel "/Docs/…"`.
  Automatické bloky `{{auto:contracts|domain-events|journey-event-types|nuget-packages}}` (balíčky z `*.deps.json`).
- **Změna chování/struktury = upravit příslušnou stránku ve stejné změně.** `DocumentationTests` hlídají odkazy na stránky
  i soubory, zmínku každého projektu ve `src/`, kontraktu, doménové události, NuGet balíčku (katalog `treti-strany/`),
  Docker image, GitHub Action, secretu/proměnné workflow a souboru v `deploy/` a `infra/`.
- Push do `main` jen se změnou `docs/prirucka/**` spustí CI i nasazení (`paths` v `ci.yml`); jiné změny jen v `docs/` a `*.md` ne.
- Sekce: přehled, architektura, EDA, toky (sekvenční diagramy), nasazení, GitHub a CI/CD, třetí strany, slovníček, historie.

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
dashboardu (v paměti, bez vzorkování) – lokálně přes AppHost, v Compose/cloudu do kontejneru `aspire-dashboard`
(cloud krok 11); bez `OTEL_EXPORTER_OTLP_ENDPOINT` se neexportuje, trace ID je jen v logu a v outboxu.
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
  Nová proměnná prostředí pro službu = přidat do `compose.yaml` (a tajnou hodnotu do `.env.example` jako vzor;
  pro produkci netajnou do `deploy/production.env`, tajnou viz Secrets níže).
  Healthcheck .NET služeb: bash `/dev/tcp` na `/health` (image nemá curl). `/health` nehlídá RabbitMQ.
- VM (krok 4): `ssh minishop-vm` (zkratka v `~/.ssh/config` uživatele → Tailscale IP `100.126.167.103`, uživatel `ubuntu`,
  klíč s passphrase – neinteraktivně jen přes ssh-agent). Aplikace v `/opt/minishop` (vlastník `deploy`, `.env` 600 –
  číst/měnit přes `sudo -u deploy`). Od kroku 7 image z GHCR: `.env` `IMAGE_PREFIX=ghcr.io/ftichy/`, `IMAGE_TAG=sha-<commit>`.
  Image publikuje CI jen z `main` po zelených testech (`ghcr.io/ftichy/minishop-web`, `…/minishop-inventory-api`, veřejné,
  amd64+arm64). Lokálně `IMAGE_PREFIX` prázdný → `minishop-…:local` z `build:`.
  Z PowerShellu přes `ssh "…"` pozor na `\n` a uvozovky – skript poslat souborem.
- CD (krok 8): po sloučení do `main` CI → `images` → `deploy` (`.github/workflows/deploy.yml`, environment `production`):
  runner přes Tailscale (`tag:ci`, OAuth) → `ssh deploy@100.126.167.103 "<celý SHA>"` → vynucený příkaz
  `/usr/local/bin/minishop-deploy` (= `deploy/deploy.sh`; stáhne `deploy/` z GitHubu pro commit, `pull`, `up --wait`,
  rollback). Ruční nasazení/rollback: *Actions → Deploy → Run workflow* se SHA, nebo na VM
  `sudo -u deploy minishop-deploy <SHA>`. Změna `deploy/deploy.sh` = ručně znovu nainstalovat na VM
  (**před** sloučením, pokud na změně závisí její vlastní nasazení). Změněný `Caddyfile` `deploy.sh` načte přes `caddy reload`.
  Port 22 je z internetu zavřený – SSH jen přes Tailscale. Nouzově: pravidlo 22 v security listu vrátit.
  Secrets/proměnné jen v environmentu `production`: `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET`, `DEPLOY_SSH_KEY`,
  `RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH` (krok 10), `SMTP_PASSWORD` (účty krok 5), `DEPLOY_HOST`, `DEPLOY_KNOWN_HOSTS`.
- HTTPS (krok 5): `https://minishop-tichy.duckdns.org` (reserved IP `141.144.255.238`), Caddy z `deploy/compose.cloud.yaml`
  + `deploy/Caddyfile`, na VM zapnuté přes `COMPOSE_FILE=compose.yaml:compose.cloud.yaml` v `.env`. Web není publikovaný ven.
  Basic auth (uživatel `minishop`, heslo zná jen uživatel – změna = secret `BASIC_AUTH_HASH`, viz `docs/secrets.md` R2),
  vypíná `BASIC_AUTH=off` v `deploy/production.env`. Security list: 80/443 z celého internetu (22 od kroku 8 zavřený). Volume `caddy-data` (certifikáty) nemazat.
- IaC (krok 9): `infra/` (OpenTofu, provider `oracle/oci`), spouští uživatel na PC: `tofu init "-backend-config=backend.hcl"` (v PowerShellu v uvozovkách – jinak rozdělí argument s tečkou),
  `tofu plan`, `tofu apply` (přihlášení `~/.oci/config` = `minishop-automation`). State v bucketu `minishop-tfstate` (S3 API,
  klíč `~/.oci/s3-credentials`); `backend.hcl`, `terraform.tfvars` a state nejsou v gitu. VM připraví cloud-init
  (`cloud-init.yaml.tftpl` → `bootstrap.sh.tftpl`, log `/var/log/minishop-bootstrap.log`), `deploy/deploy.sh` jde na VM
  přes `filebase64`. VM má `ignore_changes` na obraz a metadata – změna cloud-init/`deploy.sh` = `tofu apply -replace=oci_core_instance.vm`
  (ztráta dat!). Reserved IP má `prevent_destroy`. Bash v šablonách `.tftpl`: jen `$var`, ne `${var}` (to je OpenTofu).
  Nová VM: nový Tailscale auth key, IP v Tailscale adminu vrátit na `100.126.167.103`, aktualizovat `DEPLOY_KNOWN_HOSTS`,
  pak *Run workflow* Deploy (od kroku 10 cloud-init aplikaci nespouští – nemá secrets) a data `restore.sh latest` (krok 12).
- Secrets (krok 10): registr všech secrets a postupy výměny v `docs/secrets.md` (nový secret = nový řádek tam).
  Secrets aplikace jen v GitHub environmentu `production` (`RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH`); Deploy je pošle
  na stdin SSH, `deploy.sh` přijme jen známé klíče a sestaví `.env` na VM pokaždé znovu (`deploy/production.env`
  + `IMAGE_TAG` + secrets) – ruční úpravy `.env` na VM nepřežijí. Netajná konfigurace produkce = `deploy/production.env`
  (změna přes PR). Nový secret aplikace = `deploy.yml` (env + printf), `deploy.sh` (case + kontrola tvaru), registr.
  Změněné heslo RabbitMQ `deploy.sh` vymění i v běžícím RabbitMQ (`rabbitmqctl change_password`). OpenTofu nemá secrets aplikace.
  Secret scanning + push protection a Dependabot (`.github/dependabot.yml`) zapnuté; historie ověřená gitleaks.
- Monitoring (krok 11): služba `aspire-dashboard` v `compose.yaml` (OTLP `aspire-dashboard:18889` přes `OTEL_EXPORTER_OTLP_ENDPOINT`
  + `OTEL_SERVICE_NAME`, telemetrie jen v paměti; lokálně http://localhost:18888, v cloudu https://dashboard.minishop-tichy.duckdns.org
  přes Caddy vždy s basic auth). Caddy pouští bez hesla jen `/health` (web) a `/health/inventory` (sklad) – hlídá je UptimeRobot
  (keyword `Healthy`, case sensitive). Smoke test v `deploy.yml` kontroluje obě na `Healthy`. Nová služba s telemetrií =
  `<<: *otel-env` + `OTEL_SERVICE_NAME` v compose. Alarmy VM (CPU, paměť, chybějící metriky) + e-mail v `infra/monitoring.tf`
  (OCI Monitoring + Notifications, `alert_email` v `terraform.tfvars`).
- Zálohy (krok 12): `minishop-backup.timer` (02:30 UTC, uživatel `deploy`) → `/opt/minishop/backup.sh` → `backup.py`
  v kontejneru `ghcr.io/oracle/oci-cli:20261007` (online backup SQLite `ordering/journeys/inventory.db` + `/data/keys`
  → `minishop-RRRRMMDDTHHMMSSZ.tar.gz` → bucket `minishop-backups`, lifecycle 7 dní). VM se přihlašuje jako instance principal
  (dynamická skupina `minishop-vm`, politika v `infra/backup.tf`: číst + vytvářet, ne mazat). Obnova:
  `sudo -u deploy /opt/minishop/restore.sh latest|<jméno>|list` (zastaví web + inventory-api, původní soubory nechá jako
  `*.before-restore-…`). Skripty záloh přináší `deploy.sh` z gitu (volitelné soubory – starší commity je nemají),
  jednotky systemd cloud-init. Hlídač zálohy: healthchecks.io (`HEALTHCHECKS_BACKUP_URL`, volitelný secret).
  Nová DB/soubor s daty = přidat do `DATABASES` v `backup.py`. Před rizikovou akcí ruční záloha `sudo systemctl start minishop-backup`.
- CI (krok 6): workflow „CI“, job „Build a testy“ (= povinný status check v rulesetu). Repozitář `ftichy/MiniShop` je veřejný –
  nic tajného do gitu. `main` jen přes PR se zeleným CI (bez přímého push). SDK určuje `global.json` (10.0.200, `latestFeature`).
  Nový projekt/závislost = ověřit, že CI na Linuxu projde.

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Kontejnerizace a produkční konfigurace (Dockerfile multi-stage, uživatel `app`, data v `/data`, Data Protection klíče) | hotovo | `docs/kroky-cloud/krok-01-kontejnery.md` |
| 2. Docker Compose (`deploy/compose.yaml`, healthchecky, volumes, `.env`) | hotovo | `docs/kroky-cloud/krok-02-compose.md` |
| 3. Cloudový účet, rozpočet, IAM (OCI Frankfurt, compartment `minishop`, uživatel `minishop-automation`, OCI CLI z Dockeru) | hotovo (kvóta `minishop-quota`: v `minishop` jen A1 do 2 OCPU / 12 GB, od kroku 9 i `…-regional-count`) | `docs/kroky-cloud/krok-03-ucet-oci.md` |
| 4. Síť a VM ručně, první nasazení (VCN, security list, VM A1 Ubuntu 24.04, Docker, image přes `docker save` + `scp`) | hotovo | `docs/kroky-cloud/krok-04-sit-a-vm.md` |
| 5. Doména, HTTPS a zámek (DuckDNS `minishop-tichy.duckdns.org`, reserved IP, Caddy + Let's Encrypt, basic auth) | hotovo | `docs/kroky-cloud/krok-05-https.md` |
| 6. CI – build a testy (`.github/workflows/ci.yml`, `global.json`, veřejný repozitář, ruleset `main-protection`) | hotovo | `docs/kroky-cloud/krok-06-ci.md` |
| 7. Image do registry (GHCR, job `images`: PR zkušební build, `main` push x86+ARM, tagy `sha-…`/`latest`, veřejné) | hotovo | `docs/kroky-cloud/krok-07-registry.md` |
| 8. CD – automatické nasazení (`deploy.yml`, Tailscale, uživatel `deploy` s vynuceným příkazem, rollback) | hotovo | `docs/kroky-cloud/krok-08-cd.md` |
| 9. Infrastruktura jako kód (OpenTofu `infra/`: síť, VM, cloud-init, import reserved IP, state v OCI bucketu) | hotovo (ruční VM/VCN smazané, vše z `tofu apply`) | `docs/kroky-cloud/krok-09-iac.md` |
| 10. Secrets a konfigurace (secrets jen v GitHub `production` → stdin → `deploy.sh`, `production.env`, rotace RabbitMQ, registr `docs/secrets.md`, secret scanning, Dependabot) | hotovo (heslo RabbitMQ vyměněno pipeline, ověřeno objednávkou) | `docs/kroky-cloud/krok-10-secrets.md` |
| 11. Monitoring a logy v cloudu (Aspire dashboard v Compose, `/health` pro UptimeRobot, alarmy OCI v `infra/monitoring.tf`) | kód hotový, čeká na provedení (PR, UptimeRobot, alarmy, zkouška výpadku) | `docs/kroky-cloud/krok-11-monitoring.md` |
| 12. Zálohy a obnova po havárii (noční online backup SQLite → OCI bucket přes instance principal, `restore.sh`, healthchecks.io, cvičná havárie) | hotovo (cvičná havárie: VM smazaná → tofu apply → Deploy → `restore.sh latest`, objednávky zpět) | `docs/kroky-cloud/krok-12-zalohy.md` |

## Stav uživatelských účtů (viz `docs/Ucty-roadmap.md`)
Role User (objednává, vidí a ruší jen své) a Admin (sklad, produkty, vyřízení, všechny objednávky a uživatelé, neobjednává).
Modul Accounts (ASP.NET Core Identity, `users.db`). Souhrny kroků do `docs/kroky-ucty/`. Každý krok se nasadí do cloudu
(DB na volume, `backup.py`, secrets) – basic auth v Caddy zůstává do kroku 9.
- Fallback policy = přihlášený: **nová veřejná stránka/endpoint potřebuje `[AllowAnonymous]`**, admin/zákaznické
  `[Authorize(Policy = AuthorizationPolicies.RequireAdmin/RequireCustomer)]` (`Web/Accounts`). Handlery stránky
  Objednávky ověřují policy v kódu (`IAuthorizationService`). **Nová stránka/akce/endpoint = řádek
  v `AuthorizationMatrixTests`** (Web v paměti přes `MiniShopWebFactory`, typ `InventoryApiClient` – `Program` je dvojznačný).
- Vlastník objednávky (krok 3): `IOrderService` dostává `OrderAccess` (čtení, zrušení) a `OrderCustomer` (založení)
  parametrem – skládá je Web z cookie (`User.ToOrderAccess()/ToOrderCustomer()` ve `Web/Accounts`). Cizí objednávka
  = `NotFoundException` (404). Jméno a e-mail objednávky jen z účtu, ne z formuláře/JSON.
- E-maily (krok 5): `IEmailSender` (`Application/Abstractions`) → `Infrastructure/Email` (MailKit; bez `Email:Host`
  jen log). Lokálně Mailpit v AppHostu, produkce Gmail `ftichy.gemini@gmail.com` (`EMAIL_*` v `production.env`,
  secret `SMTP_PASSWORD`, volitelný). E-maily účtu hned (`Web/Accounts/AccountEmails`), objednávek za outboxem
  (`EmailOrderNotifier`). Testy Webu: `MiniShopWebFactory.Emails` (`RecordingEmailSender`). Do logu jen předmět e-mailu.
- První admin: produkce `docker compose exec web dotnet MiniShop.Web.dll create-admin --email …` (jen bez admina),
  vývoj `AdminBootstrap` v `appsettings.Development.json` (`admin@minishop.local` / `admin-heslo-pro-vyvoj`, vynucená změna hesla).
- Správa uživatelů (krok 6): `AccountAdministration` (Infrastructure/Accounts) – role výhradní User ↔ Admin, blokace = lockout
  do `DateTimeOffset.MaxValue`, vždy zůstane aktivní admin, ne sám sobě, rozhoduje DB (ne cookie), vše v transakci,
  každá změna = nový security stamp + `AccountAuditEntries`. `SecurityStampValidator` 1 minuta. Stránky `/Admin/Users`
  (složka `/Admin` zamčená i konvencí `AuthorizeFolder`). Útrata z Orderingu `GetCustomerSummariesAsync` (Web spojí přes ID).

| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Účty, registrace, přihlášení (modul Accounts, Identity, `users.db`) | hotovo | `docs/kroky-ucty/krok-01-ucty.md` |
| 2. Role a první admin (policies, fallback policy, `create-admin` / `AdminBootstrap` ve vývoji, matice oprávnění) | hotovo | `docs/kroky-ucty/krok-02-role.md` |
| 3. Vlastník objednávky (`CustomerId`, `OrderAccess`/`OrderCustomer`, filtr v SQL, cizí = 404, jméno a e-mail z účtu) | hotovo | `docs/kroky-ucty/krok-03-vlastnik-objednavky.md` |
| 4. Zrušení objednávky i ve stavu Pending (nový přechod ságy, kompenzace) | odloženo (analýza souběhu: dohnat zrušení v orchestrátoru, `Status` jako concurrency token) | — |
| 5. Skutečné e-maily (MailKit, Mailpit lokálně, Gmail SMTP v cloudu, potvrzení e-mailu, reset hesla) | hotovo | `docs/kroky-ucty/krok-05-emaily.md` |
| 6. Administrace uživatelů (přehled, povýšení, blokace, poslední admin, audit, EF migrace `users.db`) | hotovo | `docs/kroky-ucty/krok-06-sprava-uzivatelu.md` |
| 7. Zabezpečení skladu (servisní klíč pro zápis do Inventory.Api) | plán | — |
| 8. Zpevnění (lockout, rate limiting, bezpečnostní hlavičky) | plán | — |
| 9. Otevření webu (vypnutý basic auth, ochrana registrace, smazání účtu) | plán | — |
| 10. Přihlášení přes Google | plán | — |

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
