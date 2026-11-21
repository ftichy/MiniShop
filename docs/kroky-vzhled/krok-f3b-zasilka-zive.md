# Vzhled krok F3b: Průběh zásilky živě

Po kroku F3a se stránka obnoví hned při každé změně **stavu objednávky**. Kroky dopravce ale stav objednávky nemění
(objednávka je celou dobu „odeslaná“) – průběh zásilky na detailu objednávky a v Expedici obnovovala jen pojistka.
Teď každý krok dopravce pošle vlastní signál.

## Jak to funguje

```
webhook dopravce / dohledání → Shipment.ApplyTracking: vyšší pořadí a JINÝ stav než dosud
   → doménová ShipmentStatusChanged (stav v okamžiku změny) → outbox modulu Shipping → ShipmentStatusChangedV1 (fanout)
   → rozeslání každé kopii Webu (F3a) → LiveUpdatesNotifier → order:{id}, staff:fulfilment
   → detail objednávky (průběh zásilky) a Expedice (stav od dopravce) se obnoví hned
duplicita / pozdní zpráva / doplnění historie (26E) → žádný signál (ukáže pojistka)
```

- Doručení a vrácení dál řídí `ShipmentDeliveredV1` / `ShipmentReturnedV1` (objednávka); `ShipmentStatusChangedV1` je
  jen pro stránky – žádný modul ji nezpracovává.
- Pojistné dotazování se spojením prodlouženo z 30 s na **60 s** – signál teď chodí pro všechno, co výřezy ukazují.
- Platba (`/Pay`) zůstává u dotazování 3 s – ptá se jen pár sekund, než se platba založí; nový kontrakt v Payments
  kvůli tomu nedává smysl (záměrné rozhodnutí).

## Nové soubory

- `src/MiniShop.Contracts/Shipping/ShipmentStatusChangedV1.cs`.

## Změněné soubory

- `src/MiniShop.Domain/Shipping/Shipment.cs` – `ApplyTracking` vyvolá `ShipmentStatusChanged` při skutečné změně stavu;
  `Events/ShipmentEvents.cs` – nová událost.
- `src/MiniShop.Infrastructure/Shipping/ShippingDbContext.cs` – převod do outboxu.
- `src/MiniShop.Web/LiveUpdates/LiveUpdatesNotifier.cs` – kontrakt v seznamu rozesílaných, cíle detail + Expedice.
- `src/MiniShop.Web/wwwroot/js/live-region.js` – pojistka 60 s; `Ui/LiveRegionTagHelper.cs` – komentář.
- Testy: `OrderServiceTests.Shipping` (signál na každý krok, duplicity a mimo pořadí nejvýš jednou, doplněný mezikrok bez
  signálu), `LiveUpdatesHubTests` (cíle).
- Příručka: `eda/zive-aktualizace.md`, `architektura/moduly/shipping.md`, `architektura/vzhled.md`; CLAUDE.md (i ověření F3a).

## Rozhodnutí

- **Signál jen při skutečné změně stavu zásilky** – doplnění historie (pozdní webhook, dohledání ztraceného mezikroku)
  ukáže pojistka; jde o vzácné případy.
- **Pojistka 60 s** místo 30 s.
- **Platba bez signálu** (3 s, krátce).

## Známá omezení

- Doplněný mezikrok v historii zásilky se na otevřené stránce ukáže až pojistkou (do 60 s).

## Jak ověřit

- `dotnet test` – `DopravceDoruci_WebhookyPosouvajiStav_ObjednavkaDorucena` (4 signály), `WebhookyDvakratAMimoPoradi_*`,
  `UkoncenaZasilka_ZtracenyMezikrok_*`, `LiveUpdatesHubTests.Komu_*`.
- Po nasazení: detail odeslané objednávky kurýrem – průběh zásilky přibývá hned po kroku dopravce (krok 30 s),
  DevTools → Network: dotazy výřezu jen po signálu a pojistně po 60 s.

## Vyzkoušejte sami

1. Předejte objednávku dopravci a otevřete její detail – každých ~30 s přibude krok zásilky hned (WS zpráva `changed`).
2. Expedice při tom ukazuje stav od dopravce – mění se ve stejnou chvíli.
