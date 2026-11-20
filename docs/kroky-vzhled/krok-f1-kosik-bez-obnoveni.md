# Vzhled krok F1: Košík bez obnovení stránky

Tlačítko „Do košíku“ ve výloze a na detailu produktu už nenačítá celou stránku znovu. Stránka se nepohne (žádné
bliknutí ani skok nahoru), jen se přepíše počet kusů v košíku a dole se na pár sekund ukáže hláška „… je v košíku“.
První krok směru **F** (částečné obnovení stránky): F2 = stav objednávky výřezem místo `location.reload()`,
F3 = SignalR (push místo dotazování).

## Jak to funguje

```
klik „Do košíku“ → htmx pošle tentýž formulář na pozadí (POST /Cart?handler=Add + hlavička HX-Request + antiforgery token)
   → Cart/Index OnPostAddAsync: carts.AddItemAsync (pravidla košíku beze změny)
        HX-Request → Partial("_CartAddResult"):  #cart-badge, #cart-badge-tab, #cart-toast  (hx-swap-oob="true")
        bez něj    → přesměrování zpět jako dřív (bez JavaScriptu funguje stejně)
   → htmx vymění jen tyto tři prvky podle id; cart-add.js ukáže hlášku (Bootstrap toast)
nepřihlášený / vypršené přihlášení → cookie autentizace pošle HX-Redirect → celá stránka na přihlášení
```

- **htmx** = malá knihovna: do HTML se přidají atributy (`hx-post`, `hx-swap`), server vrací HTML jako dosud, jen menší.
  Žádná SPA, žádné JSON API – stránky zůstávají Razor Pages.
- **Out-of-band výměna** (`hx-swap-oob`): odpověď obsahuje prvky s `id`, které už na stránce jsou (odznak v hlavičce,
  odznak ve spodní liště, místo pro hlášku) – htmx je najde a nahradí. Na místo formuláře se nevkládá nic (`hx-swap="none"`).
- Obaly odznaků mají třídu `htmx-region` (`display: contents`) – rozložení hlavičky i lišty se nemění.
- Hláška je z TempData smazaná, jinak by se po přidání přes htmx ukázala ještě na příští stránce.

## Nové soubory

- `src/MiniShop.Web/wwwroot/lib/htmx/htmx.min.js` (2.0.4) + `LICENSE` (Zero-Clause BSD) – ověřeno stejným SHA-384 z jsDelivr i unpkg.
- `src/MiniShop.Web/wwwroot/js/cart-add.js` – zobrazení hlášky, chyba sítě/serveru.
- `src/MiniShop.Web/Pages/Shared/_CartAddResult.cshtml`, `src/MiniShop.Web/Carts/CartAddResultModel.cs` – odpověď pro htmx.
- `src/MiniShop.Web/Ui/Htmx.cs` – `Request.IsHtmxRequest()`, jména hlaviček.

## Změněné soubory

- `Pages/Cart/Index.cshtml.cs` – `OnPostAddAsync` vrací pro htmx partial.
- `Pages/Shared/_ProductCard.cshtml`, `Pages/Shop/Detail.cshtml` – formuláře s `hx-post`, `hx-swap="none"`, `hx-disabled-elt`, `data-cart-add`.
- `Pages/Shared/_Layout.cshtml` – `<meta name="htmx-config">`, obal `#cart-badge`, místo pro hlášku, skripty; `_TabBar.cshtml` – obal `#cart-badge-tab`.
- `Extensions/AccountsServiceCollectionExtensions.cs` – `HX-Redirect` místo 302 pro htmx požadavek bez přihlášení.
- `wwwroot/css/site.css` – `.htmx-region`, umístění hlášky nad spodní lištou.
- Testy: `CartPageTests` (3 nové), E2E `ShopFlow.AddToCartAsync` (čeká na odznak a hlášku, ověří adresu a pozici stránky), `MobilTests`.
- Příručka: `architektura/vzhled.md` (sekce Částečné obnovení + pravidlo), `treti-strany/knihovny.md`.

## Rozhodnutí

- **htmx, ne vlastní skript** – další případy (F2 stav objednávky) jsou jen atributy v HTML, ne další JavaScript.
- **Stejný handler a adresa** – žádný nový endpoint; autorizace, antiforgery i `AuthorizationMatrixTests` beze změny.
- **Hláška jako toast dole** – pruh nahoře zákazník po odscrollování nevidí.
- **Bezpečné nastavení htmx:** `allowEval: false`, `allowScriptTags: false`, `selfRequestsOnly: true`, bez vkládaných stylů.
- **Rozsah jen výloha a detail produktu** – stránka Košík (počet kusů, odebrání) zůstává s obnovením; mění se tam ceny
  i souhrn, dá se doplnit stejným způsobem později.

## Známá omezení

- Košík na jiné záložce prohlížeče se neaktualizuje sám (až po načtení stránky).
- `data-track` kliknutí na „Do košíku“ se nemění (`shop-add-to-cart`, `product-add-to-cart`), nová jsou `cart-toast`,
  `cart-toast-error`, `cart-toast-open`.

## Jak ověřit

- `dotnet test` – `CartPageTests.Htmx_*`; E2E v CI (*Nákup kartou*, *Expedice*, *Vratka*, *Mobil*) přidávají do košíku přes htmx.
- Po nasazení: výloha → odscrollovat dolů → „Do košíku“ → stránka zůstane, číslo u košíku se zvýší, dole hláška.

## Vyzkoušejte sami

1. Ve výloze sjeďte k poslednímu produktu a klikněte „Do košíku“ – stránka se nepohne, nahoře se změní počet kusů.
2. Na telefonu (nebo DevTools Ctrl+Shift+M) totéž – hláška je nad spodní lištou, počet se změní i v liště.
3. DevTools → Network: požadavek `Cart?handler=Add` má hlavičku `HX-Request: true` a odpověď je jen kousek HTML.
4. Vypněte JavaScript (DevTools → Settings → Debugger → Disable JavaScript) – formulář funguje postaru (přesměrování zpět).
