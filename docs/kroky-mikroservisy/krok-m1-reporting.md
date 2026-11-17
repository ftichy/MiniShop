# Mikroservisy krok M1: přehled tržeb jako samostatná služba

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). První modul vyjmutý z Webu do vlastního procesu – vzor
Inventory.Api (EDA krok 10B), tentokrát pro **odběratele událostí** (CQRS čtecí model z e-shop kroku 11).

## Jak to funguje

```
Prohlížeč ── /Admin/Sales ──► Web ── HTTP GET /api/sales (X-Api-Key) ──► reporting-api ──► PostgreSQL (schéma reporting)
                                                                              ▲
Ordering, Payments ── OrderPlacedV2, PaymentRefundedV1, … ──► RabbitMQ ───────┘ fronta minishop.reporting
```

- **Data jen ze zpráv.** Služba `MiniShop.Reporting.Api` poslouchá frontu `minishop.reporting` (stejné handlery jako
  dřív ve Webu – `SalesProjectionHandler`) a staví čtecí model ve schématu `reporting`. Nikoho se na nic neptá.
- **Web se ptá přes HTTP.** Stránka `/Admin/Sales` volá `ISalesReportApi` → `ReportingApiClient`
  (`https+http://reporting-api`, servisní klíč `REPORTING_API_KEY`, opakování a circuit breaker ze ServiceDefaults).
- **Výpadek služby nezastaví e-shop.** Zprávy počkají ve frontě; stránka místo chyby 500 ukáže „Přehled tržeb je teď
  nedostupný“ (`SalesReportUnavailableException`). Grafana to pozná alarmem `MiniShopQueueSilent` (M0).
- **Přechod bez odstávky.** Přirovnání: nová přepážka otevře dřív, než stará zavře. `deploy.sh` spustí `reporting-api`
  **před** Webem – chvíli tak frontu poslouchají staré kopie Webu i nová služba. RabbitMQ dá každou zprávu jen jednomu
  (competing consumers) a inbox ve schématu `reporting` je společný, takže nic neproběhne dvakrát. Nové kopie Webu už
  frontu neposlouchají. V Grafaně: `minishop_queue_consumers{queue="minishop.reporting"}` 2 → 3 → 1.
- **Žádná migrace dat.** Schéma `reporting` zůstává, jen migrace teď spouští služba místo Webu.

## Nové soubory

- `src/MiniShop.Reporting.Api/` – `Program.cs`, `ReportingModule.cs` (registrace handlerů, dřív ve Webu),
  `Controllers/SalesController.cs` (`GET /api/sales`, `POST /api/sales/rebuild`), `SalesReportOnStartupWorker.cs`
  (přesun z Webu), `appsettings*.json`, `Properties/launchSettings.json` (port 7182), `Dockerfile`.
- `src/MiniShop.Web/Reporting/ReportingApiClient.cs` – `ISalesReportApi`, `ReportingApiClient`, `SalesReportUnavailableException`.
- `src/MiniShop.ServiceDefaults/ApiKeyAuthentication.cs` (přesun ze skladu + `AddServiceApiKeyAuthentication`),
  `ServiceApiKeyHandler.cs` (dřív `Web/Inventory/InventoryApiKeyHandler`).
- Testy: `ReportingApi/ReportingApiTests.cs` (+ `ReportingApiFactory`), `Web/ReportingApiClientTests.cs`,
  `TestDoubles/FakeSalesReportApi.cs`.

## Změněné soubory

- `ServiceApiKey.Read(…, configurationKey)` – klíč pro každou službu (`ReportingConfigurationKey`).
- `SalesReportDto.StatusCounts` – klíč je jméno stavu (text), ne `SalesOrderStatus` z domény.
- Web: `AddReportingApiClient`, bez `AddReportingModule`/`AddReportingInfrastructure`/`SalesReportOnStartupWorker`/migrace
  `reporting`; stránka `Admin/Sales` přes `ISalesReportApi` + upozornění při výpadku; `appsettings.Development.json`.
- `AppHost.cs` (parametr `reporting-api-key`, projekt `reporting-api`), `MiniShop.slnx`.
- Nasazení: `deploy/compose.yaml` (služba `reporting-api`, volume `reporting-data`, proměnné Webu), `compose.grafana.yaml`,
  `.env.example`, `deploy/deploy.sh` (secret, `rolling-update.sh reporting-api 1` před Webem – jen když ji compose
  daného commitu má), `.github/workflows/deploy.yml` (secret), `ci.yml` (matice image + build v E2E a ZAP).
- Testy: `ModuleBoundariesTests` (Web ↛ doména/infrastruktura Reportingu, služba zná jen svůj modul),
  `WorkerRegistrationTests`, `SalesReportPageTests` (i výpadek služby), `MiniShopWebFactory`, `OrderServiceTests` (klíč stavu).
- Příručka: moduly Reporting a Inventory, Služby a kontejnery, architektura, lokální spuštění, kontejnery, více
  instancí, CI, CD, secrets, historie; `docs/secrets.md` (R17); `CLAUDE.md`.

## Rozhodnutí

- **Sdílené knihovny zůstávají** (jako sklad) – CI nasazuje všechny image z jednoho commitu.
- **Klíč pro každou službu** – uniklý klíč skladu přehled neotevře a naopak.
- **Web na službu nečeká** (`depends_on` ne) – bez ní běží, jen přehled hlásí nedostupnost.
- **Stránka Zprávy (`/Admin/Messages`) Reporting neukazuje** (jako sklad) – zprávy hlídají alarmy z G3/M0 a ztracenou
  zprávu napraví *Přestavět*. U Notifications (M2B) to nepůjde – tam přibude vzdálené zobrazení zpráv.
- **Jedna kopie služby** – zprávy při výpadku počkají; vedoucí kopie (`minishop.reporting-api`) je připravená na víc.

## Známá omezení

- Při postupném nasazení může stránka Přehled tržeb pár sekund hlásit nedostupnost (služba se vyměňuje).
- Outbox a dead-letter fronta Reportingu nejsou vidět na `/Admin/Messages` (jen v Grafaně).
- Výpadek služby přehled „zmrazí“ – po návratu se dožene ze zpráv, které na ni čekají ve frontě.

## Jak ověřit

1. CI zelené (build, testy, E2E, ZAP, tři image).
2. Před sloučením: secret `REPORTING_API_KEY` v environmentu `production`, nový `deploy.sh` na VM.
3. Po nasazení: GHCR balíček `minishop-reporting-api` je **Public**; na VM `docker compose ps` – `reporting-api` healthy.
4. *Správa → Přehled tržeb* se načte, *Přestavět* funguje, nová objednávka se v přehledu objeví.
5. Grafana: `max by (queue) (minishop_queue_consumers)` – `minishop.reporting` = 1.

## Vyzkoušejte sami

- Na VM `docker compose stop reporting-api` → stránka Přehled tržeb hlásí nedostupnost, objednávka projde; po 5 min
  alarm `MiniShopQueueSilent`. `docker compose start reporting-api` → přehled dožene objednávku z fronty.
- V Grafaně trasa objednávky: zpracování zpráv Reportingu je teď ve službě `reporting-api` (`service.name`), ne ve `web`.
