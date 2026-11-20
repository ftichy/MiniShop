# MiniShop: napojení na dopravce (e-shop krok 26)

20. 11. 2026 · František

Cíl: expedici přestane „dělat člověk“. Dnes skladník opíše dopravce a číslo zásilky a klikne „Doručeno“. U skutečného
e-shopu číslo přiděluje dopravce a o doručení rozhoduje on – hlásí ho webhookem, který může přijít pozdě, dvakrát,
mimo pořadí, nebo vůbec. Krok má naučit integraci s nespolehlivou službou třetí strany.

## Rozhodnutí

1. **Simulovaný dopravce místo skutečného.** Zásilkovna, PPL i Česká pošta chtějí obchodní účet nebo smlouvu a sandbox
   se nepodařilo ověřit. Simulátor stojí 0 Kč a umí „zlobit“ na povel. Rozhraní se navrhne tak, aby šel později
   vyměnit za skutečného dopravce (jako `FakePaymentGateway` → Stripe).
2. **Simulátor = cizí firma.** Samostatný kontejner `carrier-sim` bez referencí na projekty MiniShopu, s vlastními daty
   (soubor, ne naše databáze), bez telemetrie do Grafany. S e-shopem jen přes HTTP.
3. **Modul Shipping ve Webu, ne nová mikroservisa** – v souladu s vyhodnocením mikroservis M3 (vlastní schéma,
   outbox, inbox, fronta `minishop.shipping`).
4. **Překlad pojmů (anti-corruption layer).** Stavy dopravce (`in_transit`…) se převádějí na naše; do domény
   a kontraktů nepronikají.
5. **Webhook: nejdřív podpis, uložit, hned 200** – zpracování až z inboxu. Duplicitu pozná ID události, pozdní zprávu
   pořadí (`sequence`), ztracenou dohledání (`GET`) na vedoucí kopii.
6. **Osobní odběr zůstává ruční.** Dopravce jen pro kurýra.

## Kroky

| Krok | Co | Chování produkce |
| --- | --- | --- |
| 26A. Simulátor dopravce | `MiniShop.CarrierSimulator`: `POST /v1/shipments` (`Idempotency-Key`), `GET /v1/shipments/{číslo}`, časová osa stavů, webhooky s podpisem `Carrier-Signature`, zlobení `Carrier:Chaos`; Compose, CI image, `deploy.sh` (secrets `CARRIER_API_KEY`, `CARRIER_WEBHOOK_SECRET`). Souhrn [krok-26a](kroky-eshop/krok-26a-simulator-dopravce.md). | beze změny – simulátor jen běží |
| 26B. Vytvoření zásilky | Modul Shipping: `CreateShipmentV1` → klient dopravce (opakování, `Idempotency-Key` = ID příkazu) → `ShipmentCreatedV1` → `Order.Ship` (dopravce + číslo); trvalá chyba → `ShipmentFailedV1` → upozornění v Expedici. Tlačítko „Předat dopravci“ místo ručního čísla. Generátor provozu, E2E expedice, `/Privacy` (adresa jde dopravci). Souhrn [krok-26b](kroky-eshop/krok-26b-vytvoreni-zasilky.md). | číslo zásilky přiděluje dopravce |
| 26C. Webhooky a dohledání | `/api/shipping/carrier-webhook` (podpis, čas, inbox), stav jen dopředu, `ShipmentStatusChangedV1`, `ShipmentDeliveredV1` → `MarkDelivered`; dohledání `GET` u zásilek bez novinky; průběh zásilky na detailu objednávky; „Doručeno“ jen pro osobní odběr; zlobení zapnuté v produkci. Souhrn [krok-26c](kroky-eshop/krok-26c-webhooky-dopravce.md). | doručení řídí dopravce |
| 26D. Nedoručeno | Zlobení `UndeliverableRate` (2× nezastižen, vráceno odesílateli) → `ShipmentReturnedV1` → `Order.ReturnToSender` → událost `OrderReturnedToSenderV1` (choreografie, ne sága): sklad naskladní, Payments u karty vrátí peníze, Invoicing dobropis, Notifications e-mail, Reporting mimo tržbu. Produkce 10 %. Souhrn [krok-26d](kroky-eshop/krok-26d-vraceno-odesilateli.md). | každá ~10. zásilka se vrátí, kompenzace automaticky |

## Náklady

| | |
| --- | --- |
| Peníze | 0 Kč |
| Paměť VM | +~100–150 MiB (`carrier-sim`, `mem_limit 256m`) |
| Řady Grafany | simulátor žádné; modul Shipping pár `minishop_*` (fronta, outbox) |
| Secrets | +2 (`CARRIER_API_KEY`, `CARRIER_WEBHOOK_SECRET`) |
| Ruční kroky | 26A: secrets + nový `deploy.sh` na VM před sloučením |

## Rizika

- **Webhook z internetu.** V 26C jde webhook uvnitř sítě Compose (`http://web:8080/…`), skutečný dopravce by šel přes
  Caddy z internetu – endpoint proto musí ověřovat podpis i čas stejně, jako by byl veřejný.
- **Osobní údaje u třetí strany.** Adresa příjemce odchází dopravci (26B) – zmínit v `/Privacy`, do logů jen čísla.
- **Zaseknutá zásilka.** Když dopravce zásilku „ztratí“ (vzdané webhooky + výpadek), dohledání ji najde; zásilku,
  kterou dopravce vůbec nezná (smazaná data simulátoru), musí řešit člověk – upozornění v Expedici.
