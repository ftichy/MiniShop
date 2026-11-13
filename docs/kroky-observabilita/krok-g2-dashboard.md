# Observabilita krok G2: dashboard „Zprávy a ságy“

Plán: [Observabilita-roadmap.md](../Observabilita-roadmap.md). Cíl: jedna stránka v Grafaně, na které je hned vidět, jestli EDA část
MiniShopu žije – doručují se zprávy, nic se nehromadí, objednávky se potvrzují včas – i zpětně za 14 dní.

## Jak to funguje

Přirovnání: G1 přinesl černou skříňku (záznam všeho), G2 palubní desku. Čítače z EDA kroku 11 jsou počítadlo projetých aut,
nové metriky stavu pohled na parkoviště – kolik aut tam právě stojí.

1. **Metriky stavu:** [OperationalMetricsWorker](../../src/MiniShop.Infrastructure/Hosting/OperationalMetricsWorker.cs) (jen vedoucí kopie,
   `AddLeaderOnlyHostedService`) každých 30 s obejde zdroje `IOperationalMetricsSource`:
   - `OutboxStatusSource` – čekající a poison zprávy outboxu každého modulu (`IMessageStore`, stejná čísla jako `/Admin/Messages`),
   - `RabbitMqQueueStatusSource` – zprávy čekající ve frontě modulu (`QueueDeclarePassive`) a v dead-letter frontě,
   - `SagaStatusSource` (Ordering) – rozpracované ságy objednávek a vratek podle stavu (konečné stavy ne).
   
   Výsledek uloží do `OperationalMetrics` (singleton), odkud ho OpenTelemetry čte jako *observable gauge*. Snímek starší než
   2 min se nehlásí; po ztrátě vedení worker snímek smaže – hlásí nová vedoucí kopie.
2. **Doba potvrzení objednávky:** orchestrátor ságy po uložení potvrzení zapíše histogram `minishop.order.confirmation.duration`
   (od `Order.CreatedAtUtc`, štítek `payment`). Podklad pro SLO v G3.
3. **Dashboard jako kód:** `deploy/grafana/minishop-zpravy-a-sagy.json` – 4 řádky (Je vše v pořádku?, Zprávy, Ságy a objednávky, HTTP),
   import ručně v Grafaně; zdroj metrik se vybírá při importu (`__inputs`), takže soubor neobsahuje nic z konkrétního stacku.

## Nové soubory

- `src/MiniShop.Infrastructure/Observability/OperationalMetrics.cs` – snímek stavu, gauge, `OperationalReading`, `IOperationalMetricsSource`
- `src/MiniShop.Infrastructure/Observability/MessagingStatusSources.cs` – zdroje outbox a RabbitMQ
- `src/MiniShop.Infrastructure/Hosting/OperationalMetricsWorker.cs`
- `src/MiniShop.Infrastructure/Ordering/SagaStatusSource.cs`
- `src/MiniShop.Application/Ordering/Saga/OrderingTelemetry.cs` – histogram doby potvrzení (zdroj `MiniShop.Ordering`)
- `deploy/grafana/minishop-zpravy-a-sagy.json` – dashboard
- `tests/MiniShop.Tests/Infrastructure/OperationalMetricsTests.cs`

## Změněné soubory

`RabbitMqDeadLetterService` (`GetBacklogCountsAsync`), `MessagingServiceCollectionExtensions` (registrace), `OrderingInfrastructure`
(zdroj ság), `OrderSagaOrchestrator` (zápis doby potvrzení), testy `OrderServiceTests`, `MessageMaintenanceTests`, `WorkerRegistrationTests`,
příručka `eda/observabilita.md` (metriky stavu), `nasazeni/monitoring.md` (dashboard a import), `historie.md`, roadmapa, CLAUDE.md.

## Rozhodnutí

- **Stav front z aplikace, ne pluginem RabbitMQ:** žádný nový port ani konfigurace v Compose; čísla sedí s `/Admin/Messages` a health checkem.
- **Měří jen vedoucí kopie:** stav je společný – dvě kopie by v Grafaně daly dvojnásobek. Dotazy v dashboardu navíc berou `max by (module|queue)`,
  takže krátký překryv starého a nového vedoucího po převzetí nic nezdvojí.
- **Aktivní stavy vždy, i s nulou:** v grafu je „0“, ne díra. Konečné stavy (Completed, Rejected…) se nehlásí – jen by rostly.
- **Doba potvrzení až po uložení:** opakovaná zpráva (souběh, chyba uložení) nezapíše měření dvakrát.
- **U karty se počítá i zadávání karty zákazníkem** – proto štítek `payment` a velké číslo v dashboardu jen bez karty.
- **Ruční import dashboardu:** bez dalšího tokenu se zápisem do Grafany; automatické nahrání z CI jde přidat později.
- **Štítky bez ID:** přibude řádově 150 řad (7 modulů × 2, fronty × 2, 8 stavů ság, histogram ~12 košů × 3 platby).

## Známá omezení

- Stav se měří každých 30 s – krátké výkyvy (zpráva v outboxu na 1 s) graf nemusí zachytit; čítače ano.
- Po převzetí vedení může v grafu chybět jedno kolo (do 30 s).
- Jména metrik v Grafaně (tečky → podtržítka, `_total`, `_seconds`) ověřit po nasazení – panel „No data“ = jiné jméno.

## Jak ověřit

- `dotnet test` – `OperationalMetricsTests` (čerstvost snímku, ztráta vedení, selhaný zdroj), `MessageMaintenanceTests.MetrikyStavu_…`,
  `OrderServiceTests.Metriky_SagyPodleStavu_ADobaPotvrzeniObjednavky`, `WorkerRegistrationTests`.
- Po nasazení: import dashboardu, objednávka → „Potvrzené objednávky za hodinu“ + 1, „Doba potvrzení“ bod, ságy `Reserved` + 1.

## Vyzkoušejte sami

1. Objednejte na dobírku a sledujte, jak se v dashboardu pohne řádek „Ságy a objednávky“.
2. Na `/Admin/Messages` je stejné číslo poison/dead-letter jako v řádku „Je vše v pořádku?“.
3. Změňte časový rozsah na *Last 7 days* – historie zůstává i po nasazení nové verze.
