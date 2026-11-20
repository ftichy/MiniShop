# E-shop krok 26E: Provoz dopravce

Plán: [Doprava-roadmap](../Doprava-roadmap.md). Úklid po napojení na dopravce: úplná historie zásilky i po ztraceném
webhooku, oprava dohledání (neznalo stavy z 26D) a dopravce v Grafaně – dashboard a alarm na zaseknuté zásilky.

## Jak to funguje

```
Webhooky: převzato ✓ · na cestě ✗ (ztratil se) · doručuje se ✓ · doručeno ✓   → stav Delivered, historie 3 ze 4
ShipmentReconciliationWorker (každou minutu, vedoucí kopie Webu) → GetQuietAsync:
   a) na cestě (Shipment.OnTheWayStatuses) a 2 min bez novinky          – jako 26C, teď i DeliveryFailed / ReturningToSender
   b) ukončená (FinishedStatuses) s dírou v historii (Tracking.Count < LastSequence), 2 min po poslední novince,
      od té doby nedohledávaná                                           – NOVÉ, jednou
   → GET /v1/shipments/{číslo} → ApplyTracking: nižší pořadí jen do historie (stav, objednávka ani zprávy beze změny)

OperationalMetricsWorker (30 s, vedoucí kopie) → ShipmentStatusSource → Grafana:
   minishop.shipment.active{status} · minishop.shipment.finished{status} (24 h) · minishop.shipment.stale (15 min ticha)
   → dashboard řádek „Dopravce“, alarm MiniShopShipmentStale (> 0 po 10 min)
```

**Proč díry vznikaly:** dohledání se ptá až po 2 minutách ticha, ale zásilka se hýbe každých 30 s – dokud jede, ticho
nenastane, a po konci se dohledání ukončených zásilek neptalo. **Proč se ptá jen jednou:** doplněný stav posune
`LastCarrierUpdateAtUtc`, ale dohledání hned zapíše stejný `LastCheckedAtUtc` – podmínka „od poslední novinky nedohledávaná“
už neplatí; když dopravce data nemá, zkusí se to taky jen jednou.

**Chyba z 26D:** seznam stavů „na cestě“ byl v doméně (`IsOnTheWay`) i v repozitáři zvlášť. Krok 26D doplnil jen doménu,
takže dohledání zásilku „nezastižen“ / „vrací se“ nikdy nenašlo (ztracené webhooky by ji nechaly viset). Teď jsou seznamy
jen v doméně (`Shipment.OnTheWayStatuses`, `FinishedStatuses`) a repozitář i metriky je přebírají.

## Nové soubory

- `src/MiniShop.Infrastructure/Shipping/ShipmentStatusSource.cs` – zdroj metrik stavu zásilek.

## Změněné soubory

- `src/MiniShop.Domain/Shipping/Shipment.cs` – `OnTheWayStatuses`, `FinishedStatuses`, `HasHistoryGaps`; `MarkChecked` ukončenou
  zásilku neoznačí jako neznámou u dopravce.
- `src/MiniShop.Infrastructure/Shipping/ShipmentRepository.cs` – `GetQuietAsync` i ukončené s dírou; seznamy stavů z domény.
- `src/MiniShop.Application/Shipping/IShipmentRepository.cs` – popis.
- `src/MiniShop.Infrastructure/Observability/OperationalMetrics.cs` – tři nové metriky; `ShippingInfrastructure` – registrace zdroje.
- `deploy/grafana/minishop-zpravy-a-sagy.json` – řádek Dopravce (zaseknuté, na cestě podle stavu, doručeno / vráceno za 24 h).
- `deploy/grafana/minishop-alarmy.yaml` – `MiniShopShipmentStale`.
- `tests/MiniShop.Tests/Application/OrderServiceTests.Shipping.cs` – 3 testy.
- Příručka: `moduly/shipping.md`, `eda/observabilita.md`, `nasazeni/monitoring.md` (alarm), `nasazeni/grafana-tutorial.md` (tahák);
  `historie.md`, `Doprava-roadmap.md`.

## Rozhodnutí

- **Zaseknutá = 15 min bez novinky** (nebo neznámá u dopravce) – normální zásilka se hýbe každých 30 s, nedoručitelná je
  hotová za ~4 min. Alarm až po dalších 10 min (krátký restart simulátoru nebudí).
- **Metriky z databáze (stav), ne čítače v kódu** – jako ságy; přežijí restart, bez instrumentace v aplikační vrstvě.
  Štítek jen `status` – 9 řad (6 na cestě + 2 ukončené + 1 zaseknuté).
- **Ukončená zásilka se doplňuje jen do historie** – nic nového neoznamuje (doručení/vrácení už proběhlo).
- **Bez migrace** – podmínka `Tracking.Count < LastSequence` jde nad stávajícími sloupci.

## Známá omezení

- Zásilky ukončené před nasazením 26E s dírou v historii se dohledají také (podmínka je splněná) – jednou, při prvním kole
  po nasazení. Dopravce maže ukončené zásilky po 30 dnech; starší díra zůstane.
- „Ukončené za 24 h“ počítá podle poslední novinky od dopravce – doplnění mezikroku posune čas o pár minut.

## Jak ověřit

- `dotnet test MiniShop.slnx` – `UkoncenaZasilka_ZtracenyMezikrok_DohledaniHoJednouDoplni`,
  `Nezastizen_WebhookyPakNeprisly_DohledaniNajdeAVratiOdesilateli` (regrese 26D), `Metriky_ZasilkyPodleStavu_ZaseknuteAUkoncene`.
- Po nasazení: znovu naimportovat dashboard a alarmy (ručně v Grafaně); řádek Dopravce ukazuje data, alarm `MiniShopShipmentStale` Normal.

## Vyzkoušejte sami

1. Pusťte generátor provozu (`--customers 4 --rounds 2`) a v Grafaně sledujte řádek **Dopravce** – zásilky přecházejí
   mezi stavy, po doručení přibývá „Delivered“.
2. Najděte doručenou zásilku s kratší historií (zlobení `DropRate` 10 %) – do 3 minut po doručení se na detailu objednávky
   doplní chybějící řádek.
3. Zastavte na VM simulátor (`docker compose stop carrier-sim`) po předání zásilky – za 15 min „Zaseknuté zásilky“ zčervená,
   za dalších 10 min přijde e-mail z alarmu. Pak `docker compose start carrier-sim` – zásilka se dožene a alarm odezní.
