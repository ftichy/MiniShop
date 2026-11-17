# Mikroservisy krok M0: příprava a hlídání v Grafaně

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Kód modulů se nemění – jen hlídání, které po vyjmutí
Reportingu a Notifications (M1, M2C) začne být potřeba.

## Jak to funguje

Dnes frontu **všech** modulů kromě skladu poslouchá Web ve dvou kopiích. Situace „frontu nikdo neposlouchá“ téměř
nemůže nastat – a proto ji nic nehlídalo. Po vyjmutí bude Reporting jedna samostatná služba v jedné kopii: když spadne,
zprávy se tiše hromadí. Alarm na backlog (> 50 zpráv) by se při malém provozu MiniShopu neozval ani za den.

Přirovnání: dokud všechny přepážky pošty obsluhuje jeden úředník, stačí vědět, že je v práci. Když má každá přepážka
vlastního, je potřeba u každé vidět, jestli je obsazená.

Stav fronty měří vedoucí kopie služby, **které fronta patří** (`OperationalMetricsWorker`, observabilita G2). Proto dva alarmy:

| Situace | Co Grafana uvidí | Alarm |
| --- | --- | --- |
| služba běží, ale consumer RabbitMQ ne | `minishop_queue_consumers` = 0 | `MiniShopQueueNoConsumer` (5 min) |
| služba neběží (nebo RabbitMQ) | o frontě nepřijde nic | `MiniShopQueueSilent` (5 min) – fronta, která za poslední den hlásila stav, teď mlčí |

`MiniShopQueueSilent` funguje hned – hlídá i sklad (`minishop.inventory`), který dřív hlídal jen health check a UptimeRobot.

Druhá věc je **limit Grafana Cloud Free – 10 000 aktivních metrických řad**. Každá služba přidá řady (runtime, HTTP,
zprávy). Grafana počet vede ve zdroji `grafanacloud-usage` (`grafanacloud_instance_active_series`, ověřeno 17. 11. 2026:
~3 400). Alarm `MiniShopMetricsNearLimit` se ozve nad 8 000 po 30 minutách – postupné nasazení dočasně zdvojí řady
Webu (~6 300), to se do 30 minut srovná.

A **Alloy** (třídírna telemetrie) bral 184 z 256 MiB – limit zvednut na 384 MiB, než dostane telemetrii dalších služeb.

## Nové soubory

- `deploy/grafana/minishop-alarmy-limity.yaml` – alarm limitu řad (zdroj `grafanacloud-usage`, složka „MiniShop limity“).
- `docs/Mikroservisy-roadmap.md` – plán (M0 → M1 Reporting → M2A interní API → M2B zprávy všech služeb → M2C Notifications → M3 vyhodnocení).

## Změněné soubory

- `RabbitMqDeadLetterService.GetQueueStatusesAsync` (místo `GetBacklogCountsAsync`) – z pasivního dotazu na frontu
  vrací i `ConsumerCount` (`QueueStatus`).
- `OperationalMetrics.QueueConsumers` + `RabbitMqQueueStatusSource` – nová metrika `minishop.queue.consumers` (štítek `queue`).
- `deploy/grafana/minishop-alarmy.yaml` – `MiniShopQueueNoConsumer`, `MiniShopQueueSilent`.
- `deploy/grafana/minishop-zpravy-a-sagy.json` – panel „Posluchači front“ v řádku Zprávy (níže položené řádky posunuty).
- `deploy/compose.grafana.yaml` – Alloy `mem_limit` 384m.
- `.github/workflows/ci.yml` – promtool kontroluje oba soubory alarmů.
- `GrafanaConfigTests` – kontrola všech `minishop-alarmy*.yaml` a že je CI všechny předává promtoolu.
- Příručka: `nasazeni/monitoring.md` (alarmy, import druhého souboru), `eda/observabilita.md` (metrika).
- `docs/Observabilita-roadmap.md` (G5), `CLAUDE.md`.

## Rozhodnutí

- **Dva alarmy místo jednoho** – metriku měří sama služba, takže „služba neběží“ se nepozná nulou, ale chybějícími daty.
- **`MiniShopQueueSilent` přes `max_over_time(…[1d]) unless …`** místo `absent()` se jménem každé fronty – nová
  fronta (nová služba) se hlídá sama, bez úpravy pravidla. Cena: zrušená fronta hlásí alarm ještě do dne.
- **Alarm limitu ve zvláštním souboru a složce** – import napojí soubor na jeden zdroj dat a přepíše pravidla ve složce.
- **Práh 8 000 / 30 min** – rezerva na zásah (zeštíhlení metrik v Alloyi) dřív, než Grafana začne řady odmítat.

## Známá omezení

- `MiniShopQueueSilent` se ozve i při výpadku RabbitMQ (stav front nejde změřit) – spolu s `MiniShopTelemetryMissing`
  nebo bez něj podle toho, jestli Web běží.
- Krátce po prvním nasazení (metrika `minishop_queue_consumers` ještě nemá historii) nic nehlídá; `Silent` používá
  starší `minishop_queue_backlog`, ten historii má.
- Limit řad: Grafana řady nad limit podle všeho odmítá (aplikaci to neovlivní); placený tarif Pro by stál
  ~450 Kč/měsíc – mimo rozpočet.

## Jak ověřit

1. CI zelené (`GrafanaConfigTests`, promtool), nasazeno.
2. Na VM `docker inspect minishop-alloy-1 --format '{{.HostConfig.Memory}}'` = 402653184 (384 MiB).
3. Grafana Explore → `-prom`: `max by (queue) (minishop_queue_consumers)` – `minishop.inventory` = 1, fronty Webu = 2.
4. Import `minishop-alarmy.yaml` znovu (složka MiniShop) – 11 pravidel Normal.
5. Import `minishop-alarmy-limity.yaml` (zdroj `grafanacloud-usage`, složka MiniShop limity) – 1 pravidlo Normal.
6. Dashboard JSON importovat znovu – panel „Posluchači front“.

## Vyzkoušejte sami

- V Explore porovnejte `grafanacloud_instance_active_series` (zdroj `grafanacloud-usage`) se součtem
  `count by (job) ({__name__=~".+"})` (zdroj `-prom`) – rozdíl jsou řady, které už nepřicházejí, ale počítají se ještě jako aktivní.
- Po nasazení sledujte v `grafanacloud-usage` skok počtu řad a jeho návrat (~20 minut).
