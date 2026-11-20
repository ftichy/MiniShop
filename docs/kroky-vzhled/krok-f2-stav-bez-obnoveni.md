# Vzhled krok F2: Stav objednávky bez obnovení stránky

Stránky, kde se čeká na něco na pozadí, se už nenačítají každé 2 s celé znovu. Obnovuje se jen **živý výřez** – část
stránky se stavem – přes htmx (z kroku F1). Nic nebliká, stránka neskáče nahoru a v Expedici se nesmaže zaškrtnuté balení
ani rozepsané číslo zásilky. Navíc: detail objednávky ukazuje **průběh zásilky u dopravce živě**.

## Jak to funguje

```
<div id="order-live" live-region="@Model.IsLive">           ← LiveRegionTagHelper
   IsLive = true  → hx-get="/Orders/269" hx-trigger="every 3s" hx-select="#order-live" hx-swap="outerHTML"
   IsLive = false → bez atributů (jen data-live-region)

každé 3 s: htmx GET tatáž stránka (hlavička HX-Request) → server vykreslí celou stránku jako obvykle
   → htmx z ní vybere #order-live a vymění ho → nový výřez s IsLive = false už se neptá (konec dotazování)
live-region.js: htmx:beforeSwap – ve výřezu změněné pole / rozbalený <details> / kurzor v poli → výměnu odložit
/Pay se Stripem: dotaz z htmx a platba připravená → HX-Redirect na bránu (cizí doménu htmx na pozadí nenačte)
```

| Stránka | Výřez | Obnovuje se, dokud |
| --- | --- | --- |
| Detail objednávky | `#order-live` | čeká na sklad / platbu / vrácené zboží / vratku po částech, předává se dopravci, zásilku veze dopravce |
| Moje / všechny objednávky | `#orders-live` | některá čeká na sklad |
| Platba | `#pay-live` | platba se zakládá (Stripe: dokud nemá adresu brány) |
| Expedice | `#fulfilment-live` | zásilka se předává dopravci nebo je u něj na cestě (doručení ohlásí dopravce) |
| Vratky | `#returns-live` | je otevřená vratka |

## Nové soubory

- `src/MiniShop.Web/Ui/LiveRegionTagHelper.cs` – atribut `live-region` (a `@addTagHelper *, MiniShop.Web` v `_ViewImports`).
- `src/MiniShop.Web/wwwroot/js/live-region.js` – pojistka rozepsaného formuláře.
- `tests/MiniShop.Tests/Web/LiveRegionTests.cs` – výřez živý / hotový na detailu, v seznamu, u platby a v Expedici.

## Změněné soubory

- `Pages/Orders/Detail.cshtml(.cs)` – výřez `#order-live`, `DetailModel.IsLive` (nově i zásilka u dopravce).
- `Pages/Orders/Index.cshtml` – výřez `#orders-live` (seznam; hledání a průběh zadání zůstávají nad ním).
- `Pages/Pay/Index.cshtml(.cs)` – výřez `#pay-live`, `IsLive`, `HX-Redirect` na Stripe.
- `Pages/Admin/Fulfilment/Index.cshtml(.cs)` – výřez `#fulfilment-live` přes všechny tři fronty, `IsLive`.
- `Pages/Admin/Returns/Index.cshtml` – výřez `#returns-live`.
- `Pages/Shared/_Layout.cshtml` – `live-region.js`.
- Smazáno: `wwwroot/js/orders-reload.js`, atribut `data-reload-when-idle`.
- Testy: `FulfilmentPageTests` (při předání dopravci živý výřez), E2E `ShopFlow.WaitForLiveOrderStatusAsync` (*Nákup kartou*:
  potvrzení na otevřeném detailu bez načtení stránky).
- Příručka: `architektura/vzhled.md` (sekce Živý výřez + pravidlo), `toky/vytvoreni-objednavky.md`; CLAUDE.md.

## Rozhodnutí

- **Výřez z celé stránky (`hx-select`), ne zvláštní endpointy** – žádná nová adresa ani řádek v `AuthorizationMatrixTests`,
  výřez kreslí stejný kód. Cena: server každé 3 s vykreslí celou stránku (jako dřív obnovení, jen bez stylů a skriptů).
- **Interval 3 s** (dřív 2 s) – sága trvá ~5–8 s, rozdíl není znát.
- **Pojistka formuláře v JavaScriptu** podle stavu polí (ne „jednou sáhl = už nikdy“) – po vrácení pole do původního
  stavu se výřez zase obnovuje.
- **Živý průběh zásilky** na detailu objednávky (~2–4 min dotazování během doručení) – do kroku F3 (SignalR).
- Hláška z TempData (`StatusMessage`) zůstává ve výřezu – zobrazí se při prvním načtení, výměna ji už nemá.

## Známá omezení

- Dotazuje se i záložka na pozadí (prohlížeč ji jen zpomalí). F3 (SignalR) dotazování nahradí signálem.
- Vratky se obnovují, dokud je otevřená vratka – i dny, kdy se čeká na zboží (jako dřív obnovení stránky).
- `HX-Redirect` na Stripe pokrývá jen kód – automatický test by potřeboval Stripe ve Web testech (ověřit ručně kartou).

## Jak ověřit

- `dotnet test` – `LiveRegionTests`, `FulfilmentPageTests`; E2E *Nákup kartou* v CI.
- Po nasazení: objednávka → na detailu se „Ověřujeme sklad…“ změní na „Potvrzená“ bez bliknutí; Expedice při předání
  dopravci nebliká a zaškrtnuté balení zůstane.

## Vyzkoušejte sami

1. Objednejte a zůstaňte na detailu – stav se změní sám, stránka se nepohne (DevTools → Network: požadavky s `HX-Request`).
2. V Expedici předejte zásilku dopravci, zaškrtněte jinou objednávku k zabalení a počkejte – stav dopravce se mění,
   zaškrtnutí zůstane (pojistka výměnu odloží; po odškrtnutí se výřez zase obnovuje).
3. Detail odeslané objednávky kurýrem: průběh zásilky přibývá sám (Převzato → Na cestě → Doručuje se → Doručeno).
