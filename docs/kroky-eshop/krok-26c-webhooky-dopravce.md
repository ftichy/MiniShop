# E-shop krok 26C: Webhooky dopravce a dohledání

Plán: [Doprava-roadmap](../Doprava-roadmap.md). Třetí krok napojení na dopravce: doručení už neklikne skladník –
ohlásí ho dopravce webhookem. Webhooky jsou nespolehlivé (dvakrát, pozdě, vůbec), proto e-shop počítá s každou variantou.

## Jak to funguje

```
carrier-sim ── POST /api/shipping/carrier-webhook (Carrier-Signature: t=…,v1=HMAC) ──► Web (kterákoli kopie)
   CarrierWebhookProcessor: podpis + čas (5 min) → JSON dopravce → naše pojmy (accepted → PickedUp …)
   ShipmentTrackingService.ApplyAsync → Shipment.ApplyTracking(sequence, stav)
        stejné sequence = duplicita (nic) · nižší = jen historie · vyšší = nový stav · Delivered → ShipmentDeliveredV1
   → 200 (souběh dvou kopií → Version → 503 → dopravce zopakuje)
Ordering: ShipmentDeliveredV1 → Order.MarkDeliveredByCarrier → OrderCompleted → OrderCompletedV1 (jako ruční doručení)

ShipmentReconciliationWorker (vedoucí kopie, 1 min): na cestě a ticho > 2 min → GET /v1/shipments/{číslo} → ApplyTracking
```

- **Nejdřív podpis, pak obsah** – stejně jako Stripe. Čas v podpisu brání přehrání zachycené zprávy.
- **`sequence` jako klíč idempotence** – žádná zvláštní tabulka zpracovaných webhooků; historie má unikátní
  zásilka + `sequence`.
- **Stav jen dopředu** – „doručuje se“, které přijde po „doručeno“, se zapíše jen do historie.
- **Dohledání** dožene ztracené i vzdané webhooky; zásilku, kterou dopravce nezná, vrátí člověku (v Expedici se
  znovu ukáže „Doručeno“).
- **Obrazovky:** Expedice místo tlačítka „Doručeno“ ukazuje stav od dopravce (s automatickým obnovením), detail objednávky
  průběh zásilky s časy. Osobní odběr a ručně odeslané zásilky beze změny.
- **Produkce:** webhooky zapnuté (`CARRIER_WEBHOOK_URL`), krok 30 s, mírné zlobení v `production.env`.

Přirovnání: SMS o balíku – některá dvakrát, některá pozdě, jedna vůbec. Pořadí poznáte podle času na SMS, a když se
dlouho nic neděje, podíváte se do sledování zásilky sami.

## Nové soubory

- `Contracts/Shipping/ShipmentDeliveredV1.cs`; doména `ShipmentTrackingEvent`, událost `ShipmentDelivered`.
- `Application/Shipping/ShipmentTrackingService.cs` (+ `CarrierTrackingUpdate`), `ShipmentQueryService.cs` (+ DTO).
- `Infrastructure/Shipping/CarrierWebhookProcessor.cs` (+ `CarrierWebhookSettings`), migrace `CarrierTracking`.
- Web: `Controllers/CarrierWebhookController.cs`, `BackgroundServices/ShipmentReconciliationWorker.cs`,
  `Shipping/ShipmentStatusLabels.cs`.
- Testy `Web/CarrierWebhookEndpointTests.cs`; nové scénáře v `OrderServiceTests.Shipping.cs`.

## Změněné soubory

- `Shipment` (stav doručování, historie, `LastSequence`, dohledání, `Version`), `ShipmentStatus` (nové hodnoty na konci),
  `IShipmentRepository`, `ICarrierGateway.GetTrackingAsync`, `CarrierApiClient` (GET + `TranslateStatus`),
  `ShippingDbContext`, konfigurace, `ShippingInfrastructure` (tajemství webhooku – bez něj se Web nespustí).
- Ordering: `Order.MarkDeliveredByCarrier`, `IsShippedByCarrier`, `OrderDto.ShippedByCarrier`, `ShipmentResultHandler`.
- Web: `Program.cs` (worker, tajemství), registrace handleru, Expedice, detail objednávky, `appsettings.Development.json`.
- Nasazení: `compose.yaml` (Web `Carrier__WebhookSecret`), `production.env` (webhook URL, krok, zlobení), AppHost
  (adresa webhooku z Aspire), `ci.yml` (E2E/ZAP: webhook URL, krok 2 s); simulátor `appsettings.Development.json`.
- Generátor provozu (předá a čeká na doručení od dopravce), E2E (`ShopFlow.ShipAndDeliverAsync` čeká na „Doručená“).
- Testy: `MiniShopWebFactory`, `WorkerRegistrationTests`, `AuthorizationMatrixTests`, `FixedTimeProvider.Set`.
- Příručka: modul Shipping, tok Expedice, dopravce, moduly, observabilita, generátor; roadmapa; `CLAUDE.md`.

## Rozhodnutí

- **Webhook dostupný i z internetu** (jako Stripe) – ochranu dělá podpis s časem, ne skrytá adresa.
- **Jen `ShipmentDeliveredV1`** – průběh čtou stránky přímo z modulu Shipping; oznámení push o průběhu později.
- **Zpracovat hned, ne přes frontu** – zápis je malý a rychlý; dopravce stejně opakuje, co neprojde.
- **Dohledání po 2 minutách ticha** (`Shipping:ReconcileAfterMinutes`), každou minutu (`Shipping:ReconcileEveryMinutes`).
- **Zlobení v produkci zapnuté** (mírně) – aby bylo vidět, že si s ním e-shop poradí.

## Známá omezení

- Zásilku nejde stornovat ani vrátit odesílateli (krok 26D).
- Výměna `CARRIER_WEBHOOK_SECRET` shodí webhooky odeslané těsně před ní (401) – dožene je dohledání.
- Zásilky předané dopravci před 26C dostanou stavy až dohledáním (simulátor je má, webhooky tehdy neposílal).

## Jak ověřit

1. `deploy.sh` ani secrets se nemění (Web dostane `CARRIER_WEBHOOK_SECRET`, které už je v `.env`).
2. Po nasazení: objednávka kurýrem → *Expedice* → Předat dopravci → „Na cestě“ se stavem od dopravce → za ~2 min
   **sama doručená**, zákazník dostane e-mail „doručeno“; detail objednávky ukáže průběh zásilky.
3. `docker compose logs carrier-sim` – „Zlobení: webhook … přijde dvakrát / se zdrží / se nepošle“; objednávka přesto
   doručená jednou. `docker compose logs web | grep Dohledáno` – dohledání ztraceného webhooku.

## Vyzkoušejte sami

- Lokálně `CARRIER_CHAOS_DROP_RATE=1` v `deploy/.env` → webhooky nechodí, doručení dožene dohledání po ~2–3 min.
- `curl -X POST …/api/shipping/carrier-webhook -d '{}'` → 401 (bez podpisu se nic nečte).
