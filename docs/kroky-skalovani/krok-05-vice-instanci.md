# Škálování krok 5: více instancí (Web ve dvou kopiích)

Plán: [Skalovani-roadmap.md](../Skalovani-roadmap.md). Po přechodu na PostgreSQL (kroky 1–4, 3C) už nic nebrání tomu,
aby Web běžel ve víc kopiích: databáze je po síti a zprávy si consumery dělí přes RabbitMQ. Jediná překážka byly
**workery s časovačem** – ty počítaly s jednou instancí (outbox by ve dvou kopiích odeslal stejnou zprávu dvakrát).

## Jak to funguje

- **Vedoucí kopie (leader lease):** `LeaderElection` drží na vlastním spojení (bez poolu) PostgreSQL advisory lock
  `pg_try_advisory_lock(hashtext('minishop.web'))`. Kdo ho má, je vedoucí; ostatní to zkouší každých 5 s. Vedoucí každých 5 s
  ověří spojení (`SELECT 1`); selže-li, vedení pustí. Zámek patří spojení – pád nebo zastavení kopie ho uvolní samo.
  Sklad má vlastní zámek `minishop.inventory-api` (jiné workery).
- **`LeaderOnlyHostedService<T>`:** po dobu vedení spustí **novou** instanci workeru, při ztrátě vedení ji zastaví.
  Registrace `AddLeaderOnlyHostedService<T>()`:
  - jen na vedoucí: `OutboxWorker`, `MessageRetentionWorker`, `OrderSagaTimeoutWorker`, `CartReminderWorker`,
    `CatalogSyncOnStartupWorker`, `SalesReportOnStartupWorker`, `PurchasesSyncOnStartupWorker`,
  - ve všech kopiích: `RabbitMqConsumersWorker` (competing consumers, duplicitu zahodí inbox) a samotná `LeaderElection`.
- **Compose:** `web` má `deploy.replicas: ${WEB_REPLICAS:-1}`, produkce `WEB_REPLICAS=2`. Kopie sdílí volume `web-data`
  – klíče Data Protection musí být společné (cookie a antiforgery token z jedné kopie platí v druhé).
- **Caddy:** snippet `web-upstream` – `dynamic a web 8080` (adresy kopií z DNS Compose, obnova po 5 s), `round_robin`,
  `fail_duration 30s` (nedostupnou kopii vyřadí), `lb_try_duration 5s` (požadavek zkusí na jiné kopii).
- **Logy:** cesta souboru v image `minishop-{Instance}-.log` → `PerInstanceLogFiles` (ServiceDefaults) dosadí jméno kontejneru
  a při startu smaže soubory zaniklých kopií starší než 7 dní (Serilog maže jen soubory se svým jménem).
- **PostgreSQL:** TCP keepalive (`tcp_keepalives_idle=30`, interval 10, 3 pokusy) – spojení kopie, která zmizela bez
  rozloučení, pozná server do ~1 min (výchozí 2 h) a uvolní zámek.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Hosting/LeaderElection.cs` | `ILeaderElection`, `LeaderElectionOptions`, volba vedoucí přes advisory lock |
| `src/MiniShop.Infrastructure/Hosting/LeaderOnlyHostedService.cs` | obal workeru + `AddLeaderElection`, `AddLeaderOnlyHostedService<T>` |
| `src/MiniShop.ServiceDefaults/PerInstanceLogFiles.cs` | `{Instance}` v cestě logu → jméno kontejneru, úklid starých souborů |
| `tests/MiniShop.Tests/Infrastructure/LeaderElectionTests.cs` | dvě kopie = jeden vedoucí, převzetí po zastavení i po ukončení spojení serverem, různé služby, obal workeru |
| `tests/MiniShop.Tests/Web/WorkerRegistrationTests.cs` | Web i sklad: workery s časovačem jen přes `LeaderOnlyHostedService` |
| `tests/MiniShop.Tests/Web/PerInstanceLogFilesTests.cs` | jméno souboru a úklid |
| `tests/MiniShop.Tests/TestDoubles/HostedServices.cs` | odebrání workerů v testech (i registrovaných továrnou) |
| `docs/prirucka/architektura/vice-instanci.md` | stránka příručky |

## Změněné soubory

- `Persistence/ModuleDatabase.cs` – `CreateConnection(pooling)` (spojení mimo EF Core).
- `Hosting/MessagingServiceCollectionExtensions.cs` – outbox a úklid přes `AddLeaderOnlyHostedService`.
- `MiniShop.Web/Program.cs`, `MiniShop.Inventory.Api/Program.cs` – `AddLeaderElection(database, "minishop.web" / "minishop.inventory-api")`, workery jen na vedoucí.
- `ServiceDefaultsExtensions.cs` – `PerInstanceLogFiles.Apply`.
- Oba `Dockerfile` – `Serilog__WriteTo__1__Args__path=/data/logs/…-{Instance}-.log`.
- `deploy/compose.yaml` (replicas, keepalive PostgreSQL), `deploy/Caddyfile` (`web-upstream`), `deploy/production.env`
  (`WEB_REPLICAS=2`), `deploy/.env.example`.
- `MiniShopWebFactory`, `InventoryApiFactory` – `RegisteredWorkers`, odebrání přes `HostedServices`.
- Příručka: outbox (omezení), kontejnery, monitoring (logy), HTTPS a přístup, historie, slovníček.

## Generálka (lokální Compose, 2 kopie + Caddy se stejným `web-upstream`)

- Obě kopie healthy, každá vlastní soubor logu (`minishop-469c0ecb8621-20261105.log`, `minishop-6017cd1606bd-…`).
- Log: `[Vedoucí] Instance 469c0ecb8621 je vedoucí (zámek minishop.web)`, sklad `… (zámek minishop.inventory-api)`.
- Přihlášení admina přes Caddy a 4× `/Admin/Users` (střídavě obě kopie) = 200 – sdílené klíče fungují.
- `docker stop` vedoucí kopie během 60 požadavků na `/Shop`: **60× 200**, druhá kopie vedoucí **do 1 s**.
- Obnova katalogu (POST na nové vedoucí) → `SyncCatalogV1` z outboxu Webu → sklad → `ProductUpdatedV1/V2` → consumery. Zpracováno.
- Kopie znovu spuštěná = záložní. `docker kill` (tvrdý pád) vedoucí: **60× 200**, nejdelší 0,3 s, převzetí do 1 s.

## Rozhodnutí

- **Advisory lock, ne tabulka s nájmem ani Redis:** nic navíc k provozu, žádné vypršení ani hodiny – zámek zanikne
  přesně se spojením. Stejný mechanismus jako `pg_advisory_xact_lock` u posledního admina (krok 3C).
- **Vlastní spojení bez poolu:** vrácené do poolu by zámek drželo dál (Npgsql resetuje relaci až při dalším použití).
- **Nová instance workeru na každé vedení:** jednorázové kontroly po startu zopakuje i nový vedoucí po pádu původního
  (jsou idempotentní) a worker nezačíná se stavem z minulého vedení.
- **Krátký překryv dvou vedoucích je přijatelný:** správnost drží inbox a concurrency tokeny, zámek jen šetří práci.
- **Consumery ve všech kopiích:** fronta rozdělí zprávy – víc kopií = víc paralelního zpracování.
- **`dynamic a` místo `web:8080`:** statické jméno by Caddy přeložil jen na jednu adresu; dynamický seznam se po nasazení
  (nové IP kopií) obnoví sám.
- **Produkce 2 kopie, výchozí 1:** lokální Compose dál na portu 8080 bez změny.

## Známá omezení

- Nasazení restartuje obě kopie naráz (`up --wait`) – krátká odstávka při deployi zůstává.
- Rate limiting formulářů účtu je v paměti každé kopie (reálně 2× vyšší limit).
- Sklad běží v jedné kopii; obě kopie Webu jsou na jedné VM (pád VM = výpadek, viz krok 6).
- Změna `command` PostgreSQL znamená při prvním nasazení restart kontejneru databáze (pár sekund).

## Jak ověřit

```bash
dotnet test MiniShop.slnx --filter "FullyQualifiedName~LeaderElectionTests|FullyQualifiedName~WorkerRegistrationTests"
```

Produkce (VM):

```bash
cd /opt/minishop
sudo -u deploy docker compose ps web                      # dvě kopie, healthy
sudo -u deploy docker compose logs web | grep Vedoucí     # která je vedoucí
```

## Vyzkoušejte sami

1. V `deploy/.env` nastavte `WEB_REPLICAS=2` a `WEB_PORT=8080-8081`, `docker compose up -d --build`.
2. `docker compose logs web | grep Vedoucí` – zjistěte vedoucí kopii (`web-1` nebo `web-2`).
3. `docker stop minishop-web-<N>` vedoucí kopie a do 5 s se v logu druhé objeví „je vedoucí“.
4. Na druhé kopii (port 8081/8080) založte produkt ve skladu – v nabídce se objeví (outbox běží na nové vedoucí).
5. `docker start` zastavenou kopii – zůstane záložní.
