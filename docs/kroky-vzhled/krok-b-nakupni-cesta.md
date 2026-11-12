# Vzhled krok B: Nákupní cesta

Košík, pokladna, objednávky a stránky účtu dostaly stejný moderní vzhled jako výloha (krok A) a pohodlně se ovládají
na telefonu. Funkčně se nic nezměnilo – formuláře, adresy, ID polí a `data-track` zůstaly, sledování i přehrávání
pokladny (Journeys) funguje beze změny. Další krok: C – administrace.

## Jak to funguje

- **Košík:** řádky jako karty (větší náhled 96×72, dostupnost, množství, cena za kus a celkem), souhrn vpravo
  (na počítači `position: sticky`), na telefonu spodní lišta s cenou a *K pokladně*. Prázdný košík má vlastní kartu.
- **Pokladna:** dva sloupce – formulář (Doprava, Platba, Adresa) a souhrn se slevovým kódem; na telefonu souhrn nahoře
  (`order-lg-*`), tlačítko *Objednat* přes celou šířku na konci formuláře. Volby jako `.choice-card`
  (celá karta je `label`, zvýraznění `:has(input:checked)`), `fieldset` + `legend` zůstaly kvůli trackeru.
- **Přehrání pokladny** (`Replay.cshtml`): kopie formuláře má stejné karty, aby přehrání vypadalo jako originál.
- **Objednávky:** seznam i detail bez tabulek (na telefonu se nic neusekne); detail má průběh jako časovou osu,
  údaje (adresa, zásilka, platba, faktura, dobropis) v kartě; jemné barvy štítků stavů.
- **Účet:** `Pages/Account/_ViewStart.cshtml` → `_AccountLayout` (úzká karta uprostřed s logem); staré sloupce uvnitř
  karty srovná CSS (`.auth-card .row > [class*="col-"]`), hlavní tlačítka přes celou šířku, Google také.
- Pole formulářů jsou bílá (dřív převzala šedé pozadí stránky).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/Pages/Shared/_AccountLayout.cshtml` | rámeček stránek účtu – karta uprostřed, přeposílá sekce |
| `src/MiniShop.Web/Pages/Account/_ViewStart.cshtml` | layout pro celou složku Account |

## Změněné soubory

- `Pages/Cart/Index.cshtml`, `Pages/Checkout/Index.cshtml`, `Pages/Orders/Index.cshtml`, `Pages/Orders/Detail.cshtml`,
  `Pages/Orders/Replay.cshtml`, `Pages/Shared/_ExternalLoginButtons.cshtml`.
- `Orders/OrderStatusLabels.cs` – jemné barvy štítků; `wwwroot/css/site.css` – karty košíku, souhrn, spodní lišta,
  `.choice-card`, časová osa, `.auth-card`, bílá pole.
- Test `CartPageTests.NahledVKosiku_MalyIBezNovehoCss` – rozměry náhledu 96×72 (pořád přímo v HTML).
- Příručka: Vzhled a mobil (sekce Nákupní cesta), historie.

## Rozhodnutí v tomto kroku

- **Tlačítko Objednat zůstalo uvnitř sledovaného formuláře** (ne v souhrnu vpravo) – tracker zapisuje kliky a odeslání
  jen ve formuláři pokladny; souhrn je proto na telefonu nahoře a formulář s tlačítkem pod ním.
- **Layout pro složku Account** místo úprav 13 stránek – jedno místo, nové stránky účtu dostanou vzhled automaticky.
- **Bez tabulek na stránkách zákazníka** – seznamy (`ul`) se na úzkém displeji zalomí, tabulky by přetékaly.

## Známá omezení

- Administrace (Produkty, Expedice, Vratky, přehledy…) má zatím jen nové barvy a hlavičku – krok C.
- Spodní lišta košíku je `sticky` v rámci obsahu stránky (na konci stránky se zařadí nad historii košíku).

## Jak ověřit

- `dotnet test MiniShop.slnx` (stránky košíku a pokladny, Journeys – přehrání, matice oprávnění, CSP).
- Lokálně v DevTools (telefon): košík → *K pokladně* ve spodní liště → pokladna (karty dopravy a platby) → *Objednat* →
  detail s časovou osou; přihlášení a registrace v kartě uprostřed.
- Snímky: headless Edge přes PowerShell `Start-Process -Wait` (z Git Bash se Edge občas ukončí dřív, než snímek uloží);
  stránky za přihlášením stažené s cookie a doplněným `<base href>`.

**Nasazeno 12. 11. 2026** (PR #83, postupné nasazení, jen Web). Ověřeno uživatelem na telefonu: košík se spodní lištou,
pokladna s kartami dopravy a platby, objednávky a časová osa, stránky účtu.

## Vyzkoušejte sami

1. Na telefonu dejte do košíku dvě věci – v košíku je dole lišta s cenou a *K pokladně*.
2. V pokladně klepněte kamkoli na kartu „Kurýr na adresu“ – vybere se celá karta.
3. Objednejte a otevřete detail – průběh objednávky je časová osa, poslední stav zvýrazněný.
4. Jako admin přehrajte pokladnu té objednávky (Objednávky → ▶ Přehrát) – přehrání vypadá stejně jako nová pokladna.
