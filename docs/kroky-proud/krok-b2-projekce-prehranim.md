# Proud krok B2: Projekce přehráním proudu

Plán: [Proud-roadmap](../Proud-roadmap.md). Nový čtecí model **Rychlost vyřízení** ve službě Reporting.Api, postavený
**jen přehráváním proudu** `minishop.events` – trvalý čtenář si pamatuje záložku (offset) v databázi ve stejné transakci
jako data; přestavění = záložku přetočit na začátek. Teorie: příručka [Proud zpráv](../prirucka/eda/proud-zprav.md).

## Jak to funguje

```
LeadTimeProjectionWorker (Reporting.Api, vedoucí kopie):
   záložka z StreamCheckpoints (offset, generace) → BasicConsume(x-stream-offset = offset + 1, bez záložky „first“)
   dávka (≤ 100 zpráv / 1 s): OrderStatusChangedV1 → časy stavů, ShipmentStatusChangedV1 → „vezl dopravce“, ostatní přeskočit
   → JEDNA transakce: OrderLeadTimes + záložka (concurrency token Version); generace jiná = přestavění → číst od „first“
LeadTimeReportService: p50/p95 po dnech (český čas, den konce kroku, 14 dní) – potvrzení, expedice, doručení (jen dopravce)
přestavění (POST /api/lead-times/rebuild): v jedné transakci smazat OrderLeadTimes + záložka na začátek + generace + 1
Web /Admin/LeadTimes (sklad, podpora, účetní) ──HTTP + servisní klíč──► Reporting.Api /api/lead-times
```

## Nové soubory

- `src/MiniShop.Domain/Reporting/OrderLeadTime.cs` – `OrderLeadTime` (časy, jen první výskyt) a `StreamCheckpoint` (záložka, generace).
- `src/MiniShop.Application/Reporting/LeadTimeReportService.cs` (+ `ILeadTimeRepository`, DTO, percentily), `LeadTimeProjection.cs`.
- `src/MiniShop.Infrastructure/Reporting/LeadTimeProjectionWorker.cs`, `LeadTimeRepository.cs`, `Configurations/LeadTimeConfiguration.cs`,
  migrace `LeadTimes` (jen dvě nové tabulky).
- `src/MiniShop.Reporting.Api/Controllers/LeadTimesController.cs`.
- `src/MiniShop.Web/Pages/Admin/LeadTimes/Index.cshtml(.cs)`.
- Testy: `Domain/OrderLeadTimeTests.cs`, `Infrastructure/LeadTimeProjectionTests.cs` (RabbitMQ + PostgreSQL – Testcontainers),
  `Web/LeadTimesPageTests.cs`.

## Změněné soubory

- `ReportingDbContext` (DbSety), `ReportingInfrastructure` (repozitář), `ReportingModule` (služba), Reporting.Api `Program.cs` (worker).
- `RabbitMqEventStream.OffsetOf` – offset z hlavičky (sdílí čtečka B1 i čtenář B2).
- Web: `ReportingApiClient` (+ `ILeadTimeApi`), registrace, menu Přehledy (i pro sklad a podporu).
- Testy: `ReportingApiTests`, `StaffAreaTests`, `AuthorizationMatrixTests`, `WorkerRegistrationTests`, `FakeSalesReportApi`.
- Příručka: `eda/proud-zprav.md` (trvalý čtenář, diagram), `eda/cqrs.md` (srovnání přestavění), `moduly/reporting.md`.

## Rozhodnutí

- **Vstup `OrderStatusChangedV1` + `ShipmentStatusChangedV1`.** V plánu byla jen první událost; stav objednávky ale neříká,
  jestli zásilku vezl dopravce (osobní odběr = čekání na zákazníka) – to pozná projekce podle kroku zásilky v proudu.
  Kontrakty se neměnily.
- **Záložka ve stejné transakci, dávky 100 zpráv / 1 s** – „přesně jednou“ bez inboxu.
- **Přestavění přes generaci + concurrency token** – bez restartu služby, i během čtení.
- **Stránka pro role s přehledem objednávek** (sklad, podpora, účetní), v menu Přehledy.
- **Statistika podle dne konce kroku**, p50/p95 metodou nejbližšího pořadí, 14 dní.

## Známá omezení

- Data jen od nasazení B1 (21. 11. 2026) – starší zprávy v proudu nejsou; přestavění přehraje nejvýš 14 dní.
- `OrderLeadTimes` se nemaže (staré řádky v přehledu nejsou vidět – okno 14 dní); velikost zanedbatelná.
- Zpoždění čtenáře za proudem se do Grafany neměří (AMQP neřekne, kde proud končí); na stránce je „poslední zpráva“.

## Jak ověřit

- `dotnet test` – `LeadTimeProjectionTests` (Docker: restart od záložky, přestavění), `OrderLeadTimeTests`, `ReportingApiTests`, stránka.
- Po nasazení: Správa → Přehledy → **Rychlost vyřízení** – po pár objednávkách časy potvrzení; záložka roste.

## Vyzkoušejte sami

1. Pusťte generátor provozu (`--customers 4 --rounds 2`) a sledujte Rychlost vyřízení – potvrzení ~5–8 s, doručení ~2 min.
2. **Přestavět z proudu** – data na chvíli zmizí a vrátí se stejná; „přestavěno“ se zvýší o 1.
3. V logu Reporting.Api: „Projekce Rychlost vyřízení čte proud od first (generace N)“ – přehrání od začátku.
