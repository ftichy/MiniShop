# Vzhled krok A: Kostra a výloha

MiniShop dostal moderní vzhled a dá se pohodlně používat na telefonu. Krok A předělal to, co vidí každý návštěvník:
hlavičku, patičku, úvod, výlohu a detail produktu. Funkčně se nic nezměnilo – formuláře, adresy, `data-track`
i testy zůstaly. Další kroky: B – nákupní cesta (košík, pokladna, objednávky, účet), C – administrace.

## Jak to funguje

- **Design systém** = CSS proměnné v `site.css` (`--ms-brand` indigo `#4f46e5`, pozadí, zaoblení, stíny, dotyková
  plocha 44 px), které přepisují výchozí hodnoty Bootstrapu 5.3. Písmo systémové.
- **Hlavička:** na telefonu logo, košík a „hamburger“ (Bootstrap `collapse`), na počítači menu v řádku; hledání přímo
  v hlavičce (`/Shop?q=…`); administrace v jedné nabídce *Správa*, účet v nabídce se jménem.
- **Bootstrap JS** (`bootstrap.bundle.min.js` 5.3.3) lokálně – otisk SRI ověřen proti oficiálnímu vydání; je to soubor,
  takže CSP `script-src 'self'` platí dál.
- **Výloha:** karta produktu jako partial `_ProductCard` (i na úvodu), na telefonu dvě karty vedle sebe; filtry
  `offcanvas-lg` (na telefonu vysouvací panel), štítky kategorií; formulář je dál GET bez nutnosti JavaScriptu.
- **Úvod** pro zákazníka (hlavní nabídka, výhody, kategorie, 4 doporučené produkty z kopie katalogu) místo technického
  přehledu (ten je v Dokumentaci a patičce).
- **Detail produktu:** drobečková navigace, obrázek a nákupní box (cena, dostupnost, Do košíku, doprava, vrácení).
- **Ikony** jako inline SVG (`Ui/Icons.cs`) – bez fontu a CDN.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/wwwroot/lib/bootstrap/js/bootstrap.bundle.min.js` | Bootstrap 5.3.3 JS (menu, nabídky, panel filtrů) |
| `src/MiniShop.Web/Ui/Icons.cs` | ikony jako inline SVG |
| `src/MiniShop.Web/Catalog/ProductCardModel.cs`, `Pages/Shared/_ProductCard.cshtml` | sdílená karta produktu |
| `src/MiniShop.Web/Pages/Index.cshtml.cs` | úvod – kategorie a doporučené produkty |
| `docs/prirucka/architektura/vzhled.md` | příručka: design systém, hlavička, výloha, pravidla |

## Změněné soubory

- `wwwroot/css/site.css` – design systém, hlavička, patička, hero, karty, štítky, nákupní box.
- `Pages/Shared/_Layout.cshtml`, `_LoginPartial.cshtml`, `Components/CartBadge/Default.cshtml` – hlavička, patička, Bootstrap JS.
- `Pages/Index.cshtml`, `Pages/Shop/Index.cshtml(.cs)` (`RouteForCategory`, `Card`), `Pages/Shop/Detail.cshtml`.
- `Catalog/AvailabilityLabels.cs` – jemné barvy štítků dostupnosti; `Pages/_ViewImports.cshtml` – `MiniShop.Web.Ui`.
- Příručka: knihovny (Bootstrap JS), historie.

## Rozhodnutí v tomto kroku

- **Bootstrap zůstává** – žádný build CSS (Sass, Tailwind) ani nový framework; vlastní vzhled jen proměnné.
- **Žádné CDN ani webové fonty** – CSP a soukromí (účty krok 8), rychlost na mobilu.
- **Odkaz Objednávky** v hlavičce jen pro přihlášené (nepřihlášený by skončil na přihlášení).
- **Snímky v headless Edge** pro kontrolu – Edge neumí okno užší než ~500 px, mobil se proto fotí na 500 px.

## Známá omezení

- Košík, pokladna, objednávky, účet a administrace mají zatím jen nové barvy a hlavičku – rozložení pro mobil přijde v krocích B a C.
- Tmavý režim zatím ne (případný krok D).

## Jak ověřit

- `dotnet test MiniShop.slnx` (Web testy, `HardeningTests` – CSP, žádné inline skripty).
- Lokálně https://localhost:7180 – v DevTools (Ctrl+Shift+M) iPhone/Android: hamburger, košík v hlavičce, *Filtry* ve výloze.

## Vyzkoušejte sami

1. Na telefonu otevřete web – hlavička má logo, košík a menu ☰; menu se rozbalí pod ní.
2. Ve výloze klikněte na *Filtry* – vysune se panel; štítky kategorií jdou posouvat prstem.
3. Klikněte kamkoli na kartu produktu – otevře se detail; „Do košíku“ na kartě funguje samostatně.
