# E-shop krok 26A: Simulovaný dopravce

Plán: [Doprava-roadmap](../Doprava-roadmap.md). První ze tří kroků napojení na dopravce: postaví se „cizí firma“,
se kterou bude e-shop od kroku 26B mluvit. Chování produkce se nemění – simulátor jen běží, nikdo ho nevolá.

## Jak to funguje

```
Web (od 26B) ──POST /v1/shipments (X-Api-Key, Idempotency-Key)──► carrier-sim ── /data/carrier-state.json
             ◄──201 { trackingNumber: "CS…" }───────────────────┘      │
             ◄──webhook accepted / in_transit / out_for_delivery / delivered (Carrier-Signature, od 26C)
```

- **API** (`ShipmentsApi`): `POST /v1/shipments` založí zásilku a přidělí číslo `CS` + 10 číslic. `Idempotency-Key`
  je povinný: stejný klíč a obsah = stejná zásilka (200 + `Idempotent-Replayed`), jiný obsah = 409. Trvalé chyby 422
  s kódem (`invalid_postal_code`, `parcel_too_heavy`). `GET /v1/shipments/{číslo}` vrátí stav a historii bez adresy.
- **Časová osa** (`WebhookDispatcher`, každý tick `CarrierWorker`): k-tý stav nastane k × `StepSeconds` po vytvoření
  (30 s, lokálně 15 s). Když simulátor neběžel, dožene stavy s plánovanými časy.
- **Webhooky** jen s nastavenou `Carrier:WebhookUrl` (v 26A prázdná): tělo s `id` události a `sequence`, podpis
  `t=…,v1=HMAC-SHA256(tajemství, "t.tělo")`. Neúspěch → 10 s, 20 s, 40 s … max. 10 min, po 8 pokusech vzdá.
- **Zlobení** `Carrier:Chaos` (0–1): `FailureRate` (503, nic), `LostResponseRate` (vznikne, ale 503), `DuplicateRate`,
  `ReorderRate` (za následující), `DropRate` (vůbec). Výchozí 0.
- **Data** v souboru (zápis přes dočasný soubor + přejmenování), doručené zásilky se mažou po 30 dnech. Do logu jen
  číslo zásilky a reference.

Přirovnání: cvičná pošta. Doručuje, ale na povel ztratí potvrzení, pošle SMS dvakrát nebo pozdě.

## Nové soubory

- `src/MiniShop.CarrierSimulator/` – `Program.cs`, `CarrierOptions.cs` (+ `ChaosOptions`), `CarrierState.cs`
  (zásilka, událost, webhook, `CarrierStatuses`), `ShipmentStore.cs` (+ `ChaosDice`), `ShipmentsApi.cs`,
  `WebhookDispatcher.cs` (+ `CarrierWorker`), `appsettings*.json`, `Properties/launchSettings.json` (port 5184), `Dockerfile`.
- Testy `tests/MiniShop.Tests/CarrierSimulator/` – `CarrierSimulatorFactory` (zastavený čas, falešný e-shop
  `RecordingWebhookReceiver`), `CarrierSimulatorTests` (17 testů).
- Příručka `architektura/dopravce.md`; plán `docs/Doprava-roadmap.md`.

## Změněné soubory

- `MiniShop.slnx`, AppHost (`carrier-sim`, parametry `carrier-api-key`, `carrier-webhook-secret`), testovací projekt.
- `deploy/compose.yaml` (služba `carrier-sim`, volume `carrier-data`), `.env.example`, `deploy.sh` (secrets
  `CARRIER_API_KEY`, `CARRIER_WEBHOOK_SECRET` – volitelné, starší commity je nemají), `deploy.yml` (povinné),
  `ci.yml` (pátý image, build v E2E a ZAP + klíče v jejich `.env`), `.gitignore`.
- Příručka: kontejnery, secrets, historie; `docs/secrets.md` (R20); `CLAUDE.md`.

## Rozhodnutí

- **Žádná reference na MiniShop** (test `Simulator_NereferencujeProjektyMiniShopu`) – sdílený kontrakt by z cizí firmy
  udělal náš modul a skrýval by nesoulad pojmů, který má e-shop řešit překladem.
- **Soubor, ne PostgreSQL** – cizí firma naši databázi nemá; pro desítky zásilek stačí. Nezálohuje se.
- **Infrastruktura, ne rolling update** – krátký restart dopravce je realistický a e-shop ho musí snést.
- **Bez telemetrie** – nepřidá řady v Grafaně (mikroservisy M3/M4); chování je vidět v odpovědích a logu.
- **Podpis jako u Stripu** (čas + HMAC) – stejný vzor, jaký už MiniShop ověřuje u plateb.

## Známá omezení

- Jedna kopie (zámek v procesu) – pro simulátor stačí.
- Zásilku nejde zrušit ani změnit (skutečný dopravce by uměl) – do kroku 26D.
- Při výměně `CARRIER_WEBHOOK_SECRET` mohou neprojít webhooky odeslané těsně před výměnou (dožene dohledání v 26C).

## Jak ověřit

1. Před sloučením: secrets `CARRIER_API_KEY` a `CARRIER_WEBHOOK_SECRET` (64 hex, vlastní) v environmentu `production`,
   nový `deploy.sh` na VM (`scp` + `sudo install`).
2. Balíček GHCR `minishop-carrier-sim` vznikl při prvním pushi rovnou veřejný (převzal viditelnost veřejného repozitáře) –
   VM ho stáhla napoprvé, přepínat na Public nebylo potřeba. Kdyby pull selhal (`denied`), přepnout ručně a znovu *Run workflow*.
3. Na VM `docker compose ps` – `carrier-sim` healthy; `docker compose logs carrier-sim` – „Simulátor dopravce: krok 30 s,
   webhooky vypnuté …“.
4. E-shop se chová jako dřív (Expedice beze změny).

## Vyzkoušejte sami

- Lokálně `dotnet run --project src/MiniShop.CarrierSimulator` a v PowerShellu:
  ```powershell
  $h = @{ 'X-Api-Key' = 'vyvojovy-klic-u-dopravce-jen-pro-lokalni-beh'; 'Idempotency-Key' = 'zkouska-1' }
  $body = '{"reference":"42","recipient":{"name":"Jan","street":"Ulice 1","city":"Praha","postalCode":"110 00"}}'
  Invoke-RestMethod http://localhost:5184/v1/shipments -Method Post -Headers $h -Body $body -ContentType 'application/json'
  ```
  Zopakujte stejný příkaz (stejné číslo zásilky), pak změňte `reference` (409) a za minutu
  `Invoke-RestMethod http://localhost:5184/v1/shipments/<číslo> -Headers $h` (stavy přibývají).
