# Krok 11: Observabilita – jedna trasa přes celou ságu (OpenTelemetry)

Po kroku 10B se jedna objednávka rozpadne na ~8 zpráv ve dvou procesech a mezi nimi leží outbox (2 s) a RabbitMQ.
Stack trace ani log jedné služby neukážou, co se s objednávkou 42 dělo. Teď každá operace dostane **trace ID**
(standard W3C `traceparent`) a to ID **přežije outbox i frontu**. V Aspire dashboardu je pak celá sága vidět
jako jedna trasa: `POST /api/orders` → rezervace ve skladu → potvrzení.

## Jak to funguje

Přirovnání: zásilka na poště dostane **podací číslo**. Každá pobočka, kterou projde, si ho opíše do svého záznamu.
Podle čísla pak dohledáte celou cestu, i když zásilka přes noc ležela ve skladu. U nás je podací číslo trace ID,
pobočky jsou HTTP požadavek, outbox, RabbitMQ a handlery. Úsek na jedné pobočce je **span**.

Problém byl v místech, kde zpráva „leží“. .NET a OpenTelemetry samy propojí jen to, co běží v jednom toku
(HTTP → SQL → HttpClient). Outbox worker ale zprávu odešle o 2 s později v jiném vlákně a consumer v jiném procesu.
Podací číslo je proto potřeba **uložit ke zprávě** a příjemce z něj musí pokračovat:

```
Web                                                                     Inventory.Api
POST /api/orders  (trace ab51…, span A)
 └ SaveChanges → outbox: OrderPlaced  TraceParent=00-ab51…-A-01      ① uložit do outboxu
   ··· 2 s ···
OutboxWorker: "dispatch OrderPlaced" (rodič A)                       ② navázat podle TraceParent
 └ orchestrátor → outbox: ReserveStockV1  TraceParent=00-ab51…-B-01
   ··· 2 s ···
OutboxWorker: "publish ReserveStockV1"
 └ RabbitMQ, hlavička traceparent: 00-ab51…-C-01   ───────────────►  ③ "process ReserveStockV1" (rodič C)
                                                                      └ outbox: StockReservedV1 (ab51…)   ④ znovu ①
                                                                      "publish StockReservedV1"
"process StockReservedV1"  ◄───────────────────────────────────────── hlavička traceparent (ab51…)
 └ sága potvrdí → outbox: OrderConfirmed (ab51…) → "dispatch OrderConfirmed" → notifikace
```

1. **Uložení:** `OutboxMessageFactory` zapíše do nového sloupce `OutboxMessages.TraceParent` aktuální `Activity.Current`
   (HTTP požadavek, nebo span handleru, ve kterém zpráva vznikla).
2. **Doručení z outboxu:** `OutboxProcessor` otevře span `publish ReserveStockV1` (integrační zpráva) nebo
   `dispatch OrderPlaced` (doménová událost) a jako rodiče mu dá uložený `TraceParent`.
3. **RabbitMQ:** `RabbitMqMessage.CreateProperties` přidá hlavičku `traceparent`. `RabbitMqQueueConsumer` ji přečte
   a otevře span `process ReserveStockV1` jako jejího potomka.
4. **Řetěz se přenáší sám:** uvnitř spanu je `Activity.Current` právě ten span. Když handler uloží další zprávu,
   bod 1 jí dá stejné trace ID. Žádný handler o trasách nic neví.

Trace ID jde jen v „obálce“ (sloupec v outboxu, hlavička zprávy), **ne v kontraktu**. Kontrakty v `MiniShop.Contracts` se nezměnily.

**Kdo co dělá:**

| Kde | Co |
| --- | --- |
| `Infrastructure/Observability/MessagingTelemetry` | `ActivitySource` a `Meter` „MiniShop.Messaging“, jen třídy z .NET (`System.Diagnostics`) – Infrastructure nepotřebuje OpenTelemetry |
| `ServiceDefaults` | OpenTelemetry: poslouchá `MiniShop.*` + ASP.NET Core, HttpClient, EF Core, runtime; posílá vše přes OTLP |
| Aspire AppHost | nastaví službám `OTEL_EXPORTER_OTLP_ENDPOINT` = svůj dashboard (tohle dělal už od 10B) |

**Tři pilíře v dashboardu:**
- **Traces:** trasa `web: POST /api/orders` se spany z obou služeb (`web`, `inventory-api`), včetně SQL příkazů.
- **Metrics:** vlastní metriky `minishop.outbox.delivered`, `minishop.outbox.failed`, `minishop.outbox.delay`
  (jak dlouho zpráva čekala v outboxu, typicky 0–2 s), `minishop.consumer.handled`, `minishop.consumer.failed`.
  Tagy jsou `message.type` a `module`/`queue`. K tomu standardní metriky HTTP a runtime.
- **Structured logs:** logy obou služeb. U každého je trace ID a z logu se jde prokliknout na trasu.
  V souborovém logu je trace ID v hranatých závorkách: `[INF] [ab51b48f…] OrderEventsLogHandler: …`.

**Health check outboxu.** `/health` obou služeb vrátí **Degraded**, když v outboxu modulu leží nedoručená zpráva
starší než 5 minut. Typicky neběží broker, nebo zpráva propadla jako poison message. Běžně tam zpráva leží
nejvýš pár sekund.

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `src/MiniShop.Infrastructure/Observability/MessagingTelemetry.cs` | `ActivitySource` + `Meter`, `CurrentTraceParent()`, `StartOutboxActivity()`, `StartProcessActivity()`, `RecordError()`, čítače |
| `src/MiniShop.Infrastructure/Observability/OutboxHealthCheck.cs` | `OutboxHealthCheck<TDbContext>` – Degraded při zprávě starší 5 min |
| `tests/MiniShop.Tests/TestDoubles/ActivityRecorder.cs` | „mini OpenTelemetry“ pro testy – posluchač zdroje MiniShop.Messaging |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/Outbox/OutboxMessage.cs`, `Persistence/Configurations/OutboxMessageConfiguration.cs` | sloupec `TraceParent` (max. 64 znaků) ⚠️ změna schématu |
| `Infrastructure/Outbox/OutboxMessageFactory.cs` | zapíše `TraceParent` z `Activity.Current` |
| `Infrastructure/Outbox/OutboxProcessor.cs` | span `publish …`/`dispatch …` s rodičem z `TraceParent`, metriky doručení/chyb/zpoždění |
| `Infrastructure/Integration/RabbitMq/RabbitMqMessage.cs` | hlavička `traceparent`, `ReadTraceParent()` (RabbitMQ vrací hlavičky jako `byte[]`) |
| `Infrastructure/Integration/RabbitMq/RabbitMqQueueConsumer.cs` | span `process …` z hlavičky, metriky zpracování/chyb |
| `Infrastructure/Integration/InProcessIntegrationBus.cs` | stejný span `process …` pro každý modul (testy vypadají jako „naostro“) |
| `Infrastructure/Ordering/OrderingInfrastructure.cs`, `Inventory/InventoryInfrastructure.cs` | registrace health checku `outbox-ordering` / `outbox-inventory` |
| `Infrastructure/MiniShop.Infrastructure.csproj` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| `ServiceDefaults/ServiceDefaultsExtensions.cs`, csproj | `ConfigureOpenTelemetry()`: trasy, metriky, logy, OTLP export; balíčky `OpenTelemetry.*` 1.20.0 (EF Core 1.20.0-beta.1) |
| `Web/Program.cs`, `Inventory.Api/Program.cs` | `ClearProviders()` + Serilog `writeToProviders: true` (logy i do OpenTelemetry, na konzoli jen jednou) |
| `Web/appsettings.json`, `Inventory.Api/appsettings.json` | `[{TraceId}]` v šabloně souborového logu |
| `Web/BackgroundServices/OrderSagaTimeoutWorker.cs` | span `saga timeout` (s tagem `minishop.order.id`) |
| `tests/.../Application/OrderServiceTests.cs` | `PlaceOrder_CelaSagaJeVJedneTrase`, `PlaceOrder_MimoSledovanouOperaci_ZpravyBezTraceParent` |
| `tests/.../Infrastructure/OutboxTests.cs` | `Ulozeni_ZapamatujeSiTraceIdOperace`, `HealthCheck_NedorucenaZprava_PodleStari` (2×), `HealthCheck_StaraUzDorucenaZprava_JeHealthy` |
| `tests/.../Infrastructure/RabbitMqTests.cs` | `Zprava_VSledovaneOperaci_NeseTraceparentVHlavicce`, `Zprava_MimoSledovanouOperaci_BezHlavicek` |

## Rozhodnutí v tomto kroku

- **Trace ID místo vlastního `CorrelationId`.** Roadmapa mluví o correlation ID. `traceparent` je totéž, jen ve
  standardním formátu, kterému rozumí OpenTelemetry, dashboard i jiné nástroje (Jaeger, Grafana Tempo…).
  Vlastní ID by bylo duplicitní.
- **Trace ID v obálce, ne v kontraktu.** Je to technická metadata, ne business data. Kontrakty se nemění
  a nepotřebují novou verzi.
- **Propagace vlastním kódem, ne vestavěným tracováním RabbitMQ.Client.** Knihovna v7 to umí taky, ale outbox by
  stejně propojit nedokázala. Takhle je celý mechanismus vidět na pár řádcích (`MessagingTelemetry`).
- **Infrastructure bez balíčků OpenTelemetry.** `ActivitySource` a `Meter` jsou přímo v .NET. OpenTelemetry je jen
  „posluchač“ ve službách (ServiceDefaults) a zdroj najde podle jména `MiniShop.*`. Stejný princip jako `ILogger`
  vs. Serilog.
- **SQL spany jen uvnitř operace.** Workery se každé 2 s ptají outboxu „je tu něco?“. Bez filtru
  (`Activity.Current is not null`) by z každého dotazu vznikla samostatná trasa a dashboard by se zahltil.
  Health checky (`/health`, `/alive`) se z tras vynechávají ze stejného důvodu.
- **Health check Degraded, ne Unhealthy, a jen v `/health`.** Služba dál přijímá objednávky a restart by
  zaseknutou zprávu nevyřešil. `/alive` (podle něj orchestrátor restartuje) zůstává jen „proces žije“.
- **Bez Aspire se nic neexportuje** (chybí `OTEL_EXPORTER_OTLP_ENDPOINT`). Trace ID ale vznikají dál: jsou v logu
  i v outboxu, takže se dá dohledávat i v souborech.

## Známá omezení

- **Timeout ságy začne novou trasu** (`saga timeout`). Neví, z jakého požadavku objednávka vznikla – sága si
  `TraceParent` neukládá. Šlo by to vyřešit linkem (`ActivityLink`) na původní trasu.
- **Obnova katalogu** (`SyncCatalogV1` po startu) a **seed produktů** běží mimo požadavek. První span je až
  doručení z outboxu, každá zpráva má vlastní trasu.
- **Opakované doručení** (retry z outboxu nebo RabbitMQ) přidá do stejné trasy další span `publish`/`process`.
  To je v pořádku a je to vidět.
- **Aspire ukazuje stav jen podle HTTP kódu.** Degraded vrací 200, takže zdroj zůstane zelený. Text „Degraded“
  je vidět na `https://localhost:7180/health`.
- **Dashboard Aspire drží telemetrii jen v paměti.** Po restartu AppHostu je prázdný. Pro trvalé uložení by byl
  potřeba např. Jaeger nebo Grafana.
- **Bez vzorkování (sampling).** Ukládá se 100 % tras. Pro výuku je to ideální, v provozu by se vzorkovalo.
- Platí omezení z kroků 5–10B (jedna instance služby, retry bez backoffu, inbox se nečistí, bez dead-letter fronty).

⚠️ **Změna schématu:** tabulka `OutboxMessages` má nový sloupec `TraceParent`. **Smažte obě databáze:**
`src/MiniShop.Web/ordering.db` a `src/MiniShop.Inventory.Api/inventory.db` (+ `-shm`, `-wal`). Jinak služby po startu
spadnou na chybějícím sloupci. Obě se při startu vytvoří znovu (sklad s ukázkovými produkty, katalog se obnoví sám).

## Jak ověřit

1. `dotnet test` – projde všech 136 testů. Nové jsou:
   - `PlaceOrder_CelaSagaJeVJedneTrase`: spany `dispatch OrderPlaced` → `publish ReserveStockV1` →
     `process ReserveStockV1` → `publish StockReservedV1` → `process StockReservedV1` → `dispatch OrderConfirmed`
     mají trace ID „HTTP požadavku“. Všechny zprávy objednávky v outboxech obou modulů ho nesou taky.
   - `Ulozeni_ZapamatujeSiTraceIdOperace`, `PlaceOrder_MimoSledovanouOperaci_ZpravyBezTraceParent`,
   - `Zprava_VSledovaneOperaci_NeseTraceparentVHlavicce`, `Zprava_MimoSledovanouOperaci_BezHlavicek`,
   - `HealthCheck_NedorucenaZprava_PodleStari` (1 min → Healthy, 6 min → Degraded), `HealthCheck_StaraUzDorucenaZprava_JeHealthy`.
2. Smažte databáze (viz výše) a spusťte `dotnet run --project src/MiniShop.AppHost`. Otevřete dashboard.
3. Na `https://localhost:7180/Orders` vytvořte objednávku. V dashboardu otevřete **Traces** a vyberte trasu
   `web: POST /api/orders`. Do ~8 s se v ní objeví spany obou služeb (`web` a `inventory-api` mají jinou barvu):
   INSERT objednávky, `dispatch OrderPlaced`, `publish ReserveStockV1`, `process ReserveStockV1` (inventory-api),
   UPDATE Products, `publish StockReservedV1`, `process StockReservedV1` (web), `dispatch OrderConfirmed`.
4. **Structured logs** → filtr `Objednávka` → klikněte na trace ID u logu „Objednávka … potvrzena“. Otevře se stejná trasa.
5. **Metrics** → `web` → `minishop.outbox.delay`: histogram čekání v outboxu (typicky do 2 s).

Ověřeno i naživo bez Aspire: v souborových lozích obou služeb je u celé ságy jedno trace ID. HTTP POST, `OrderPlaced`,
`ReserveStockV1` a potvrzení jsou ve Webu, `StockReservedV1` v Inventory.Api.

## Vyzkoušejte sami

1. **Najděte trasu v souborovém logu.** Vezměte trace ID z řádku `HTTP POST /api/orders responded 202`
   v `src/MiniShop.Web/logs/minishop-RRRRMMDD.log` a vyhledejte ho i v `src/MiniShop.Inventory.Api/logs/inventory-RRRRMMDD.log`.
   Je tam rezervace té samé objednávky.
2. **Podívejte se do outboxu.** Otevřete `ordering.db` v DB Browseru: sloupec `TraceParent` má u všech zpráv
   jedné objednávky stejnou prostřední část (trace ID), liší se jen span ID.
3. **Výpadek skladu v trase.** V dashboardu zastavte `inventory-api` a vytvořte objednávku. Trasa skončí
   `publish ReserveStockV1`. Zapněte sklad do 30 s: ve **stejné** trase přibudou spany ze skladu, i když
   zpráva mezitím čekala ve frontě.
4. **Zpráva, která selhává.** Zastavte `messaging` (RabbitMQ) a vytvořte objednávku. Span `publish ReserveStockV1`
   je červený (broker nedostupný). Po 5 minutách ukáže `https://localhost:7180/health` `Degraded`.
   Po zapnutí brokeru se zpráva doručí a stav se vrátí na `Healthy`.
5. **Timeout ságy.** Při vypnutém skladu počkejte 30 s. V Traces se objeví samostatná trasa `web: saga timeout`
   (viz známá omezení) se zamítnutím objednávky.
