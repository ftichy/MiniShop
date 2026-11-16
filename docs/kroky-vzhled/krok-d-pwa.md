# Vzhled krok D: Instalovatelná aplikace (PWA)

MiniShop se dá „nainstalovat“ na plochu telefonu a chová se pak jako aplikace: vlastní ikona, spuštění přes celý displej
bez adresního řádku, úvodní obrazovka v barvě značky, spodní lišta se záložkami a bez signálu stránka „Jste offline“
místo chyby prohlížeče. Je to pořád stejný web – žádný druhý kód, žádný obchod s aplikacemi. Na Androidu z něj Chrome
sestaví skutečný balíček (WebAPK), takže aplikace je i v seznamu aplikací a odinstaluje se jako každá jiná.

Přirovnání: místo stavby druhého obchodu (nativní aplikace) dostal stávající obchod vlastní vchod přímo z plochy telefonu.

## Jak to funguje

1. **Manifest** (`wwwroot/manifest.webmanifest`) – „vizitka“ aplikace: jméno, ikony 192/512 px + *maskable* (Android si
   ji ořízne do kruhu), barvy, start `/Shop`, `display: standalone`, zástupci Košík a Moje objednávky (dlouhé podržení ikony).
   Layout na něj odkazuje `<link rel="manifest">`, k tomu favicon, `apple-touch-icon` a meta značky pro iPhone.
2. **Service worker** (`wwwroot/sw.js`) – skript na pozadí, přes který jde každý požadavek stránky („vrátný“):
   - stránky (HTML) **vždy ze sítě a nikdy do mezipaměti** (objednávky, adresy, správa – na sdíleném telefonu nesmí
     zůstat); bez sítě předem uložená `offline.html`;
   - statické soubory (`/css/`, `/js/`, `/lib/`, `/icons/`, `/media/products/`) z mezipaměti, nejvýš 150 záznamů;
   - POST, cizí weby (Stripe, Google) a ostatní cesty (`/api`) vůbec neobsluhuje.
   `PRECACHE_VERSION` = otisk offline stránky a ikony – nová verze souborů = nový service worker a úklid staré mezipaměti.
3. **Registrace a instalace** (`wwwroot/js/pwa.js`, soubor kvůli CSP): zaregistruje `/sw.js`; když prohlížeč nabídne
   instalaci (`beforeinstallprompt` – Android, Chrome, Edge), ukáže tlačítko **Nainstalovat aplikaci** v patičce a v nabídce
   účtu; na iPhonu místo tlačítka návod *Sdílet → Přidat na plochu*. Spuštěno z ikony = třída `is-standalone`.
4. **Spodní lišta** (`_TabBar`, jen pod `lg`): Obchod, Košík s počtem kusů (jen zákazník – stejné pravidlo jako v hlavičce),
   Objednávky, Účet; nepřihlášený Obchod a Přihlásit. Aktivní záložka podle adresy. Hlavička nahoře beze změny.
5. **Hlavičky:** manifest, `sw.js` a offline stránka mají `Cache-Control: no-cache` (`PwaFiles`) – prohlížeč po nasazení
   pozná novou verzi service workeru. CSP se neměnila (`default-src 'self'` pokryje manifest i worker).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/wwwroot/manifest.webmanifest` | manifest aplikace |
| `src/MiniShop.Web/wwwroot/sw.js` | service worker |
| `src/MiniShop.Web/wwwroot/offline.html` | stránka bez sítě (samostatná, bez skriptu) |
| `src/MiniShop.Web/wwwroot/js/pwa.js` | registrace, tlačítko instalace, návod pro iPhone |
| `src/MiniShop.Web/wwwroot/icons/*` | logo (SVG) a ikony PNG 32/180/192/512 + maskable |
| `src/MiniShop.Web/Ui/PwaFiles.cs` | cesty souborů PWA a hlavička `no-cache` |
| `src/MiniShop.Web/Pages/Shared/_TabBar.cshtml` | spodní lišta |
| `src/MiniShop.Web/Pages/Shared/Components/CartBadge/Tab.cshtml` | košík ve spodní liště |
| `tests/MiniShop.Tests/Web/PwaTests.cs` | manifest a ikony, pravidla service workeru, otisk, offline stránka, lišta |

## Změněné soubory

- `Pages/Shared/_Layout.cshtml` – manifest, ikony, `viewport-fit=cover`, tlačítko instalace v patičce, spodní lišta, `pwa.js`.
- `Pages/Shared/_LoginPartial.cshtml` – tlačítko instalace v nabídce účtu.
- `Carts/CartBadgeViewComponent.cs` – varianta `tab`, počet kusů jen jednou za požadavek (`HttpContext.Items`).
- `Program.cs` – `UseStaticFiles` s `PwaFiles.ApplyCacheHeaders`.
- `wwwroot/css/site.css` – `--ms-tab-bar-height`, `.tab-bar`, odsazení obsahu a lišty „K pokladně“, `is-standalone`.
- Testy: `AuthorizationMatrixTests` (soubory PWA veřejné), E2E `MobilTests.Mobil_Aplikace_SpodniListaAOfflineStranka`
  (`Session.Context` pro `SetOfflineAsync`).
- Příručka: Vzhled a mobil (sekce Instalovatelná aplikace), historie; CLAUDE.md.

## Rozhodnutí v tomto kroku

- **PWA místo nativní aplikace** – jeden kód, aktualizace hned po nasazení, žádné poplatky ani schvalování. Google Play
  jde později přes TWA z téže PWA.
- **Stránky se nikdy neukládají** – offline jen informační stránka. Uložená výloha by ukazovala staré ceny a dostupnost;
  stránky s objednávkami by na sdíleném telefonu prozradily osobní údaje.
- **Otisk souborů místo ručního čísla verze** – zapomenuté zvýšení verze by nechalo v telefonech starou offline stránku;
  `PwaTests` otisk přepočítá a napíše správnou hodnotu (bez `\r`, ať nezáleží na koncích řádků Windows/Linux).
- **Hlavička beze změny, lišta navíc** – `data-track` v hlavičce používají Journeys i E2E; na telefonu je košík vidět
  nahoře i dole, ale nic se nerozbilo.
- **Logo z existující ikony tašky** (`Icons.Bag`, Bootstrap Icons MIT) v přechodu indigo → fialová jako `.brand-mark`;
  PNG vyrenderované headless Edgem ze SVG.

## Známá omezení

- Bez sítě nejde nakupovat ani procházet výlohu – jen offline stránka.
- iPhone: instalace jen ručně přes Safari, `beforeinstallprompt` neexistuje.
- Oznámení push a zápis v Google Play nejsou součástí kroku (možné kroky E a F).
- Na telefonu je košík v hlavičce i ve spodní liště.

## Jak ověřit

- `dotnet test` – `PwaTests`, `AuthorizationMatrixTests`; E2E *Mobil – aplikace* (lišta, registrace service workeru,
  `SetOfflineAsync` → offline stránka → „Zkusit znovu“).
- Android (Chrome): otevřít https://minishop-tichy.cz → patička nebo nabídka účtu **Nainstalovat aplikaci** (případně menu ⋮ →
  *Instalovat aplikaci*) → ikona na ploše → spustit: bez adresního řádku, dole lišta. Režim letadlo → klepnout na odkaz →
  „Jste offline“.
- iPhone (Safari): *Sdílet → Přidat na plochu*.
- Počítač: Chrome/Edge DevTools → *Application* → *Manifest* (bez chyb) a *Service workers* (aktivní); *Lighthouse*.

## Vyzkoušejte sami

1. V DevTools → *Application* → *Cache storage* se podívejte, co je v `minishop-static-v1` – jen CSS, JS, ikony a obrázky,
   žádná stránka.
2. Změňte text v `offline.html` a spusťte `PwaTests` – test napíše nový `PRECACHE_VERSION`. Proč by bez něj telefon
   novou offline stránku nikdy nedostal?
3. V DevTools → *Network* zaškrtněte *Offline* a obnovte stránku.
