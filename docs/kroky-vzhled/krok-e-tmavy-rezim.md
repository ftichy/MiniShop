# Vzhled krok E: Tmavý režim

MiniShop se přizpůsobí tmavému vzhledu telefonu nebo počítače – večer nesvítí bílou stránkou do očí. Výchozí volba je
**Automaticky** (podle systému); v patičce jde vybrat **Světlý** nebo **Tmavý** natrvalo pro daný prohlížeč.

## Jak to funguje

1. **Bootstrap 5.3 umí tmavý režim sám** – stačí atribut `data-bs-theme="dark"` na `<html>` a jeho komponenty
   (formuláře, nabídky, upozornění, tabulky, panel filtrů) přepnou barvy.
2. **`wwwroot/js/theme.js`** atribut nastaví podle volby (`localStorage` klíč `minishop-theme`: `light`/`dark`, bez
   záznamu = automaticky podle `prefers-color-scheme`). Načítá se v `<head>` **před styly a bez `defer`** – kdyby běžel
   až po vykreslení, stránka by při každém načtení na okamžik bíle problikla. Při volbě Automaticky sleduje i změnu
   systému (večer se přepne sám). Mění i `meta theme-color` (barva lišty telefonu).
3. **Naše barvy** – proměnné `--ms-*` mají v `site.css` blok `[data-bs-theme=dark]` s tmavými hodnotami. Pevné barvy
   (hlavička, spodní lišta, lišta „K pokladně“, pole formulářů, záhlaví tabulek, štítky) převedené na proměnné.
   Text v barvě značky má vlastní proměnnou `--ms-brand-text` – indigo `#4f46e5` je na tmavém pozadí málo čitelné,
   jako text je `#a5b4fc`; plná tlačítka zůstávají `#4f46e5` (bílý text na nich má dobrý kontrast).
4. **Přepínač** v patičce (`_Layout`): skupina tlačítek *Automaticky / Světlý / Tmavý*, `aria-pressed` u vybrané.
5. **Ostatní:** offline stránka podle systému (`prefers-color-scheme` – bez sítě volbu z patičky nezná), diagramy Mermaid
   v dokumentaci v tmavém tématu, `btn-outline-secondary` čitelný ve tmě (Bootstrap má pevnou šedou), tlačítka
   v žlutých upozorněních `btn-warning` místo `btn-outline-dark`.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/wwwroot/js/theme.js` | nastavení `data-bs-theme`, přepínač, sledování systému |
| `tests/MiniShop.E2E/VzhledTests.cs` | E2E: automatika podle systému, ruční volba přežije obnovení |

## Změněné soubory

- `wwwroot/css/site.css` – nové proměnné (`--ms-text`, `--ms-text-subtle`, `--ms-brand-text`, `--ms-header-bg`,
  `--ms-bar-bg`, `--ms-input-bg`, `--ms-table-head-bg`, `--ms-image-bg`), blok `[data-bs-theme=dark]`, štítky, QR 2FA.
- `wwwroot/css/docs.css`, `wwwroot/js/docs.js` – kód a diagramy dokumentace ve tmě.
- `Pages/Shared/_Layout.cshtml` – `theme-color` pro oba režimy, `color-scheme`, `theme.js`, přepínač, bez `bg-white`;
  `_EmailConfirmationBanner` – `btn-warning`.
- `wwwroot/offline.html` + `sw.js` (nový `PRECACHE_VERSION`) – tmavá offline stránka.
- Testy: `PwaTests.TmavyRezim_SkriptVHlavicce_PrepinacVPaticce`. Příručka: Vzhled a mobil, GitHub CI (E2E scénáře), historie; CLAUDE.md.

## Rozhodnutí

- **Automaticky jako výchozí** – kdo má tmavý telefon, chce tmavý web; ruční volba je pro výjimky.
- **Volba jen v prohlížeči** (`localStorage`), ne v účtu – platí i pro nepřihlášené, žádná migrace; na jiném zařízení
  platí jeho systém.
- **Skript v hlavičce bez `defer`** – jediný blokující skript; je malý a v mezipaměti (service worker), bez něj by stránka
  problikla.
- **Fotky produktů na bílém** – fotky mívají bílé pozadí, tmavý rámeček by je „ořízl“.
- **E-maily a PDF světlé** – poštovní programy tmavý režim e-mailů zobrazují každý jinak; PDF se tiskne.

## Známá omezení

- Bez JavaScriptu zůstává světlý vzhled (Bootstrap se podle systému sám nepřepne).
- Přepnutí vzhledu na stránce dokumentace ji znovu načte (Mermaid diagram už vykreslený nepřebarví).
- Stránky za přihlášením (košík, objednávky, správa) jsou kontrolované jen přes sdílené proměnné – snímky v kroku
  pokrývají veřejné stránky; ověřit v produkci.

## Jak ověřit

- `dotnet test` – `PwaTests` (theme.js v hlavičce před styly, přepínač, `theme-color`), E2E *Vzhled*.
- Telefon s tmavým vzhledem → MiniShop tmavý; patička → *Světlý* → zůstane světlý i po obnovení; *Automaticky* → zase podle systému.

## Vyzkoušejte sami

1. DevTools → *Rendering* → *Emulate CSS media feature prefers-color-scheme: dark* – stránka se přepne hned.
2. V `site.css` změňte `--ms-canvas` v bloku `[data-bs-theme=dark]` – změní se pozadí všech stránek najednou.
3. Zakomentujte `<script src="~/js/theme.js">` v `_Layout` a přesuňte ho na konec `<body>` – při načtení uvidíte bílé probliknutí.
