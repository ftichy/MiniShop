# Krok 10B: Sklad jako samostatná služba (.NET Aspire)

V kroku 10A byly Ordering a Inventory oddělené v kódu i v datech, ale běžely v jednom procesu. Teď se sklad
**odstěhoval do vlastní služby** `MiniShop.Inventory.Api`. Má vlastní proces, vlastní `inventory.db` a vlastní frontu
v RabbitMQ. Web je teď služba Ordering a k tomu UI celého e-shopu. Obě služby i RabbitMQ spouští jedním příkazem
**.NET Aspire** (`MiniShop.AppHost`).

## Jak to funguje

Přirovnání: v 10A byly sklad a prodejna dvě oddělení v jedné budově s vlastními šanony. Teď se sklad
přestěhoval do vlastní budovy. Pošta (RabbitMQ) chodí stejně, jen na jinou adresu. Když sklad na den zavře,
prodejna dál přijímá objednávky. Ty čekají ve schránce skladu, a když se sklad otevře, vyřídí je.
A na stav zásob se prodejna může skladu zeptat telefonem (HTTP), ale prodej na tom nestojí.

```
                         MiniShop.AppHost (.NET Aspire) – spustí vše, předá adresy, dashboard
                                │                    │                       │
        ┌───────────────────────┴───┐   ┌────────────┴──────────┐   ┌────────┴─────────────────────┐
        │ MiniShop.Web  (7180)      │   │ RabbitMQ (kontejner)   │   │ MiniShop.Inventory.Api (7181) │
        │ Ordering + UI             │   │                        │   │ Inventory                     │
        │ ordering.db               │──►│ ReserveStockV1 ───────►│──►│ fronta minishop.inventory     │
        │ fronta minishop.ordering  │◄──│◄── StockReservedV1, …  │◄──│ inventory.db                  │
        │ fronta minishop.audit     │   │    ProductUpdatedV1    │   │                               │
        │                           │   └────────────────────────┘   │                               │
        │ InventoryApiClient ───────────── HTTP GET/POST /api/products ─────►│ ProductsController     │
        └───────────────────────────┘                                └───────────────────────────────┘
```

**Dva druhy komunikace, každý na něco jiného:**

| | Zprávy (RabbitMQ) | HTTP (InventoryApiClient) |
| --- | --- | --- |
| K čemu | business tok: rezervace, uvolnění, změny katalogu | administrace produktů a zobrazení zásob |
| Když sklad neběží | zprávy počkají ve frontě, sága doběhne později (nebo timeout) | stránka ukáže „sklad nedostupný“ |
| Spojení služeb | volné, služby o sobě nemusí vědět | těsnější, ale jen pro UI |

Proto se **objednávky nesmí opírat o HTTP**. Formulář nové objednávky bere produkty a ceny z vlastní kopie katalogu
(`IOrderService.GetCatalogAsync`), zásoby si ze skladu jen „přibere“, když odpovídá.

**Co dělá Aspire (AppHost)?** V `AppHost.cs` je popsaná celá aplikace jako kód: RabbitMQ, služba `inventory-api`
a služba `web`, která ji potřebuje (`WithReference`). Aspire:
- spustí RabbitMQ v Dockeru (persistentní kontejner `messaging-…`, přežije restart AppHostu) a vygeneruje mu heslo,
- předá oběma službám connection string `ConnectionStrings:messaging` (`amqp://…`),
- předá Webu adresu skladu (`services:inventory-api:https:0`), kterou si přečte service discovery,
- počká, až je RabbitMQ připravený (`WaitFor`), a hlídá `/health` obou služeb,
- otevře dashboard se stavem služeb, logy, proměnnými prostředí a odkazy.

**Service discovery.** `InventoryApiClient` má adresu `https+http://inventory-api`. To není skutečná URL, ale jméno
služby. Skutečnou adresu doplní service discovery: pod Aspire z proměnných, které nastaví AppHost, bez Aspire ze sekce
`Services` v `appsettings.json` Webu (`https://localhost:7181`).

**Odolnost HTTP (resilience).** Každý HttpClient dostal ze `ServiceDefaults` standardní pipeline:
- **timeout pokusu** 2 s, celkový timeout 5 s,
- **retry**: 1 opakování, ale jen u GET (POST/PUT/DELETE se neopakují, aby se třeba produkt nezaložil dvakrát),
- **circuit breaker** („jistič“): když za 30 s selže aspoň polovina z min. 4 požadavků, na 15 s se sklad přestane
  volat a požadavek hned skončí chybou. Stránka se pak načte za pár milisekund místo čekání na timeouty.

**Kdo poslouchá kterou frontu.** Každá služba zaregistruje jen handlery svého modulu, takže `RabbitMqConsumers`
vytvoří jen jejich fronty: Web `minishop.ordering` (+ `minishop.audit`), Inventory.Api `minishop.inventory`.
Auditní frontu poslouchá jen Web (`AuditQueue` je v Inventory.Api prázdná). Jinak by se o zprávy přetahovaly
dva consumery a každá služba by v logu viděla jen část.

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `src/MiniShop.AppHost/` (`AppHost.cs`, csproj, `launchSettings.json`) | Aspire orchestrátor: RabbitMQ + `inventory-api` + `web` |
| `src/MiniShop.ServiceDefaults/ServiceDefaultsExtensions.cs` | `AddServiceDefaults()` (service discovery, resilience, health checky) a `MapDefaultEndpoints()` (`/health`, `/alive`) |
| `src/MiniShop.Inventory.Api/Program.cs` | služba skladu: jen modul Inventory, outbox worker, consumer `minishop.inventory`, seed produktů |
| `src/MiniShop.Inventory.Api/GlobalExceptionHandler.cs` | výjimky → ProblemDetails (404/400), kopie z Webu – každá služba má vlastní |
| `src/MiniShop.Inventory.Api/appsettings.json`, `Properties/launchSettings.json` | `inventory.db`, RabbitMQ bez auditu, porty 7181/5181, log `logs/inventory-RRRRMMDD.log` |
| `src/MiniShop.Web/Inventory/InventoryApiClient.cs` | `IProductService` přes HTTP; 404 → `NotFoundException`, 400 → `DomainException` |
| `src/MiniShop.Web/Inventory/InventoryUnavailableException.cs` | sklad neodpověděl (neběží, timeout, 5xx, otevřený jistič) |
| `src/MiniShop.Infrastructure/Hosting/MessagingServiceCollectionExtensions.cs` | `AddMessaging()` – RabbitMQ + consumery + outbox worker pro libovolnou službu |
| `src/MiniShop.Infrastructure/Ordering/OrderingDatabaseInitializer.cs` | vytvoření `ordering.db` |
| `src/MiniShop.Infrastructure/Inventory/InventoryDatabaseInitializer.cs` | vytvoření `inventory.db` + ukázkové produkty |
| `tests/.../Web/InventoryApiClientTests.cs` | 7 testů klienta (falešný `HttpMessageHandler`, bez sítě) |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| **přesuny** (`git mv`) | `Web/Controllers/ProductsController.cs` → `Inventory.Api/Controllers/`; `Web/BackgroundServices/OutboxWorker.cs`, `RabbitMqConsumersWorker.cs` a `Web/Extensions/MessagingServiceCollectionExtensions.cs` → `Infrastructure/Hosting/` |
| `Infrastructure/DependencyInjection.cs` | nové `AddInfrastructureCore()`; `AddInfrastructure(ordering, inventory)` = jádro + oba moduly, používají ho už jen testy |
| `Infrastructure/DatabaseInitializer.cs` | smazán – rozdělen po modulech |
| `Infrastructure/MiniShop.Infrastructure.csproj` | `Microsoft.Extensions.Hosting.Abstractions`, `Configuration.Binder` (workery a `AddMessaging`) |
| `Infrastructure/Integration/RabbitMq/RabbitMqOptions.cs`, `RabbitMqConnection.cs` | `ConnectionString` (od Aspire, má přednost), `ClientName` (jméno spojení v UI), `AuditQueue` volitelná |
| `Infrastructure/Integration/RabbitMq/RabbitMqModuleConsumer.cs` | `RabbitMqConsumers` vynechá audit, když je `AuditQueue` prázdná |
| `Application/Ordering/IOrderService.cs`, `OrderService.cs`, `OrderContracts.cs` | `GetCatalogAsync()` + `CatalogItemDto` – nabídka z kopie katalogu |
| `Application/Ordering/ICatalogProductRepository.cs`, `Infrastructure/Ordering/CatalogProductRepository.cs` | `GetAvailableAsync()` |
| `Web/Program.cs` | `AddServiceDefaults`, jen `AddOrderingInfrastructure`, `AddMessaging` z Infrastructure, `MapDefaultEndpoints` |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | místo `AddInventoryModule` → `AddInventoryApiClient` (typovaný HttpClient na `https+http://inventory-api`) |
| `Web/appsettings.json` | bez connection stringu `Inventory`, sekce `Services:inventory-api`, `ClientName` |
| `Web/Pages/Orders/*` | nabídka z kopie katalogu, zásoba ze skladu jen když odpovídá, varování při výpadku |
| `Web/Pages/Products/Index.cshtml.cs` | `InventoryUnavailableException` → hláška místo chyby 500 |
| `Web/MiniShop.Web.http` | produkty na `{{inventory}}` (7181) |
| `tests/.../Architecture/ModuleBoundariesTests.cs` | `Infrastructure.Hosting` ve společném jádru; `Web_NesahaDoVnitrkuSkladu`, `InventoryApi_NezavisiNaOrderingu` |
| `tests/.../Infrastructure/RabbitMqTests.cs` | connection string má přednost; consumery jen pro moduly služby, audit volitelný |
| `MiniShop.slnx`, `README.md`, `.vscode/launch.json` | nové projekty, spouštění přes Aspire |

## Rozhodnutí v tomto kroku

- **Knihovny Application/Infrastructure zůstávají sdílené.** Obsahují kód obou modulů a každá služba si zaregistruje
  jen svůj. Rozdělit je na projekty po modulech (`MiniShop.Inventory.Domain` …) půjde později. Hranice v kódu
  už teď hlídají testy architektury. Sdílet knihovny je jako sdílet poštovní řád: obě budovy podle něj
  píšou dopisy, ale do cizích šanonů to nikoho nepustí.
- **UI zůstává jedno (ve Webu)** a produkty spravuje přes HTTP API skladu. Synchronní volání je v pořádku, protože
  jde o administraci a zobrazení. Business tok (sága) jde dál přes zprávy.
- **Objednávka se neopírá o HTTP.** Nabídka produktů je z kopie katalogu, takže objednávat jde i při výpadku skladu.
  Vedlejší efekt: nový produkt se ve formuláři objeví až po promítnutí do katalogu (~2–4 s). Chyba 404 „není
  v katalogu“ z kroku 10A tím zmizela.
- **RabbitMQ spravuje Aspire** jako persistentní kontejner s vlastním heslem (uložené v user secrets AppHostu).
  `minishop-rabbit` z kroku 8 zůstává pro spouštění bez Aspire. Navzájem si nevadí, Aspire používá jiný port.
- **Transport `InProcess` už pro spuštění nejde.** Dva procesy si zprávy v paměti nepředají, takže `AddMessaging`
  ho odmítne s vysvětlením. V testech zůstává: tam jsou oba moduly v jednom procesu.
- **`GlobalExceptionHandler` je ve službě skladu jako kopie.** Každá služba si chyby překládá sama a může se vyvíjet
  nezávisle. Sdílet by ho šlo, ale ServiceDefaults by pak musely znát doménové výjimky.
- **Retry jen u GET.** POST/PUT/DELETE nejsou idempotentní. Kdyby odpověď jen nedorazila, opakování by akci provedlo podruhé.
- **OpenTelemetry (traces, metriky v dashboardu) zatím ne**, to je krok 11. Dashboard teď ukazuje stav služeb a konzolové logy.

## Známá omezení

- **Bez Dockeru se aplikace nespustí** (RabbitMQ). Testy Docker nepotřebují.
- **Web musí znát adresu skladu.** Bez Aspire je zapsaná v `appsettings.json` (`Services:inventory-api`).
  Změníte-li port Inventory.Api, změňte ho i tam.
- **První dvě načtení stránky při výpadku skladu trvají ~4 s** (2 pokusy × 2 s timeout), pak se vypne jistič a stránky
  se načítají hned. Po 15 s jistič zkusí sklad znovu.
- **Zásoby ve formuláři objednávky jsou jen informativní** a můžou být chvilku staré. O skutečném přidělení rozhoduje
  rezervace v sáze.
- **Obě služby používají stejné knihovny** – změna ve sdíleném jádru (outbox, bus) vyžaduje nasadit obě služby.
- Platí omezení z kroků 5–10A: jedna instance každé služby, retry outboxu bez backoffu, inbox se nečistí,
  consumery bez dead-letter fronty.

⚠️ **Databáze:** `inventory.db` je teď ve `src/MiniShop.Inventory.Api/`. Starou `src/MiniShop.Web/inventory.db`
(+ `-shm`, `-wal`) smažte. Smažte i `src/MiniShop.Web/ordering.db`, aby kopie katalogu odpovídala novému skladu
(jinak v ní můžou zůstat produkty, které nový sklad nezná). Po smazání jen `ordering.db` zůstávala nabídka
objednávek prázdná – opraveno obnovou katalogu, viz [doplněk níže](#doplněk-obnova-kopie-katalogu-synccatalogv1).

## Jak ověřit

1. `dotnet test` – projde všech 123 testů (s doplňkem o obnově katalogu 128). Nové jsou:
   - `InventoryApiClientTests` (7×): úspěch, adresa a metoda, 404, 400, validační chyba, 500, nedostupná služba,
   - `ModuleBoundariesTests`: `Web_NesahaDoVnitrkuSkladu`, `InventoryApi_NezavisiNaOrderingu`, `SpolecneJadro_…(Infrastructure.Hosting)`,
   - `RabbitMqTests`: `Bus_ConnectionString_MaPrednostPredHostNameAPortem`, `Consumery_JenFrontyModuluSluzbyAVolitelneAudit` (2×).
2. `dotnet run --project src/MiniShop.AppHost` → v konzoli je odkaz na dashboard (`https://localhost:17180/login?t=…`).
   V dashboardu jsou tři zdroje `messaging`, `inventory-api`, `web`, všechny **Running / Healthy**.
3. `https://localhost:7180/Orders`: v nabídce jsou produkty se zásobou. Objednávka se do ~4–8 s potvrdí a na
   `https://localhost:7181/api/products` klesne zásoba.
4. V management UI RabbitMQ (odkaz u zdroje `messaging` v dashboardu) jsou na záložce *Connections* dvě spojení
   `MiniShop.Web` a `MiniShop.Inventory.Api`. Frontu `minishop.inventory` konzumuje jen Inventory.Api.

## Vyzkoušejte sami

1. **Vypněte sklad.** V dashboardu u `inventory-api` klikněte na *Stop*. Na stránce Objednávky se objeví varování
   a „zásoba nedostupná“, stránka Produkty ukáže hlášku. Objednávka ale projde a zůstane „Ověřujeme sklad…“.
   V RabbitMQ UI uvidíte ve frontě `minishop.inventory` čekající zprávu `ReserveStockV1`.
2. **Zapněte ho do 30 s** (*Start*). Sklad zprávu z fronty vyřídí a objednávka se potvrdí. Žádná zpráva se neztratila.
3. **Zapněte ho až po 30 s.** Timeout ságy objednávku zamítne. Když se sklad vrátí, zboží stejně zarezervuje
   (zpráva ve frontě čekala), sága ale už má objednávku zamítnutou, a tak pošle `ReleaseStockV1` (kompenzace).
   Zásoba je nakonec stejná jako před objednávkou.
4. **Jistič.** Při vypnutém skladu několikrát obnovte stránku Objednávky. První dvě načtení trvají ~4 s, další
   jsou okamžitá. V logu Webu je `Polly: Resilience event occurred. EventName: 'OnCircuitOpened'`.
5. **Bez Aspire.** Zastavte AppHost, spusťte `docker start minishop-rabbit` a obě služby zvlášť
   (`dotnet run --project src/MiniShop.Inventory.Api`, `dotnet run --project src/MiniShop.Web`). Funguje to stejně,
   adresy jsou z `appsettings.json`.

---

## Doplněk: obnova kopie katalogu (`SyncCatalogV1`)

**Problém.** Po smazání `ordering.db` (při běžícím skladu s produkty) zůstal formulář objednávky bez produktů.
Kopie katalogu se plní jen událostmi `ProductUpdatedV1`, a ty sklad posílá jen při založení nebo změně produktu.
Prodejna přišla o ceník a sklad posílá oběžníky jen při změně cen – takže si ho už nikdy nedoplní.

Je to obecná vlastnost EDA: **read model (kopie dat) plněný jen změnami se sám znovu postavit neumí.**
Potřebuje způsob, jak si říct o celý aktuální stav (resync, „state transfer“).

**Jak to funguje.**

```
Web (start, kopie prázdná)            RabbitMQ                    Inventory.Api
CatalogSyncOnStartupWorker
  └ CatalogSyncService ── SyncCatalogV1 (příkaz, přes outbox) ──►  SyncCatalogHandler
                                                                     └ pro každý produkt
CatalogProjectionHandler ◄────────── ProductUpdatedV1 ×N ◄────────── (přes outbox skladu)
  └ CatalogProducts doplněny
```

- **Po startu Webu** `CatalogSyncOnStartupWorker` zkontroluje kopii katalogu. Když je prázdná, pošle skladu příkaz `SyncCatalogV1`.
- **Ručně**: tlačítko *Obnovit katalog ze skladu* na stránce Objednávky. Hodí se, když v kopii chybí jeden produkt
  (třeba zpráva skončila po 5 neúspěšných pokusech).
- **Sklad** odpoví `ProductUpdatedV1` pro každý produkt. Projekce je zpracuje jako každou jinou změnu.
- **Dokud katalog nedorazí**, stránka Objednávky ukáže „Katalog produktů se načítá ze skladu…“ a sama se obnovuje.

**Proč je to bezpečné opakovat:**
- Každá znovu poslaná zpráva má **nové `EventId`**. Inbox Orderingu ji tak nezahodí jako duplicitu.
- Čas změny ve zprávě je **skutečný čas poslední změny produktu** (`Product.ChangedAtUtc`), ne „teď“.
  Kdyby nesla „teď“, mohla by přebít novější změnu ceny, která je k Orderingu zrovna na cestě.
  Takhle `CatalogProduct.Apply` starší stav vždycky odmítne.
- Žádost jde přes **outbox** Orderingu. Když sklad nebo broker neběží, počká ve frontě a vyřídí se později.

**Nové soubory:**

| Soubor | Co obsahuje |
| --- | --- |
| `Contracts/Inventory/SyncCatalogV1.cs` | příkaz pro sklad „pošli celý katalog“ (`CommandId`, `OccurredAtUtc`) |
| `Application/Inventory/SyncCatalogHandler.cs` | sklad: `ProductUpdatedV1` pro každý produkt |
| `Application/Ordering/Catalog/CatalogSyncService.cs` | `RequestIfEmptyAsync()` (po startu), `RequestAsync()` (tlačítko) |
| `Web/BackgroundServices/CatalogSyncOnStartupWorker.cs` | jednorázová kontrola po startu Webu |

**Změněné soubory:**

| Soubor | Změna |
| --- | --- |
| `Domain/Inventory/Product.cs`, tabulka `Products` | nový sloupec `ChangedAtUtc` (čas poslední změny názvu/ceny) ⚠️ změna schématu |
| `Application/Ordering/ICatalogProductRepository.cs`, `Infrastructure/Ordering/CatalogProductRepository.cs` | `AnyAsync()` |
| `Web/Program.cs`, `Extensions/ApplicationServiceCollectionExtensions.cs` | `CatalogSyncOnStartupWorker`, `CatalogSyncService` |
| `Inventory.Api/Program.cs` | handler `SyncCatalogV1` v modulu Inventory |
| `Web/Pages/Orders/*` | hláška při prázdném katalogu (+ automatické obnovení stránky), tlačítko *Obnovit katalog ze skladu* |
| testy | `ZtracenaKopieKatalogu_ObnoviSeZeSkladu`, `PlnaKopieKatalogu_PoStartuSeSkladuNeptaSe`, `ObnovaKatalogu_NeseCasPosledniZmenyANoveEventId`, `Zmena_SiZapamatujeCasPosledniZmeny`, `SyncCatalogV1_TvarJsonuSeNezmenil` |

**Rozhodnutí:**
- **Příkaz (pull), ne pravidelné rozesílání.** Sklad nemusí vědět, kdo kopii drží, ani ji pravidelně posílat všem.
  Kdo kopii ztratí, řekne si o ni.
- **Znovu se posílají běžné `ProductUpdatedV1`, žádný nový „snapshot“ kontrakt.** Projekce už je umí zpracovat,
  opakování i pořadí ošetřuje.
- **Automaticky jen při prázdné kopii.** Při každém startu by se zbytečně posílal celý katalog.

**Známá omezení:**
- **Obnova produkty nemaže.** Smazané produkty už ve skladu nejsou, takže o nich sklad nic nepošle. Když kopie
  zmeškala `ProductRemovedV1`, produkt v ní zůstane, dokud se nepošle znovu. Řešilo by to „snapshot“ se seznamem
  všech ID.
- Při úplně novém startu (obě databáze prázdné) dorazí produkty dvakrát: ze seedu skladu a z obnovy. Neškodí to.

⚠️ **Změna schématu:** tabulka `Products` má nový sloupec `ChangedAtUtc`. **Smažte `src/MiniShop.Inventory.Api/inventory.db`**
(+ `-shm`, `-wal`), jinak služba skladu po startu spadne na chybějícím sloupci. `ordering.db` mazat nemusíte –
katalog si teď obnoví sama.

**Vyzkoušejte sami:** spusťte AppHost, v dashboardu zastavte `web`, smažte `src/MiniShop.Web/ordering.db` a `web`
znovu spusťte. Na stránce Objednávky se na chvíli ukáže „Katalog produktů se načítá…“ a do ~3 s jsou produkty zpět.
V logu Webu je `[Katalog] Kopie katalogu je prázdná – žádám sklad o celý katalog (SyncCatalogV1)`.
