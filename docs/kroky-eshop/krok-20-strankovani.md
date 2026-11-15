# E-shop krok 20: Stránkování seznamů

Cíl: seznamy v e-shopu a administraci se při větším počtu záznamů nenačítají celé – po stránkách, s volitelnou velikostí
stránky a hledáním tam, kde je záznamů nejvíc (uživatelé, objednávky).

## Jak to funguje

Přirovnání: telefonní seznam – nikdo nečte všechna jména najednou; listuje se po stránkách a hledá se podle jména.

1. **Jádro:** `PageRequest` (stránka + velikost z adresy, opraví nesmysly, `ClampTo` stránku za koncem) a `PagedList<T>`
   (položky, celkový počet, „Zobrazeno X–Y z N“) v `Application/Abstractions`; v Infrastructure `ToPagedListAsync`
   (nejdřív `COUNT`, pak `OFFSET/LIMIT` nad seřazeným dotazem).
2. **Komponenta:** partial `_Pager` + `PagerModel` (okno odkazů ‹ 1 … 4 5 [6] 7 8 … 20 ›, výběr *Na stránku* 10/20/50/100,
   `pager.js` formulář po výběru odešle). Adresy `?p=2&size=50` zachovají ostatní parametry (hledání).
3. **Stránky:** Objednávky (v DB, hledání podle čísla `?order=`), Uživatelé (v DB, hledání v e-mailu `?q=`, útrata jen pro účty na
   stránce), Faktury a Hodnocení (v DB – dřív jen posledních 100), Produkty a Slevové kódy (v paměti), Historie přihlášení
   (v DB – dřív jen posledních 20). Akce na stránce vrací na stejnou stránku.

## Nové soubory

- `src/MiniShop.Application/Abstractions/Paging.cs` – `PageRequest`, `PagedList<T>`, `ToPagedList`
- `src/MiniShop.Infrastructure/Persistence/PagingExtensions.cs` – `ToPagedListAsync`
- `src/MiniShop.Web/Ui/PagerModel.cs`, `Pages/Shared/_Pager.cshtml`, `wwwroot/js/pager.js`
- `tests/MiniShop.Tests/Web/PagingTests.cs`

## Změněné soubory

`IOrderRepository`/`OrderRepository` + `IOrderService`/`OrderService` (`GetPageAsync`, `GetCustomerSummariesAsync(customerIds)`),
`AccountAdministration.GetAccountsPageAsync`, `LoginAttemptRecorder.GetPageAsync`, faktury a hodnocení (`GetPageAsync` místo
`GetLatestAsync`), stránky Orders/Index, Admin/Users/Index + Detail, Admin/Invoices, Admin/Reviews, Admin/Coupons, Products/Index,
Account/LoginHistory, `_Layout` (pager.js), testy (`UserAdministrationTests` hledá místo celé první stránky, `OrderServiceTests`),
příručka `architektura/vzhled.md`, CLAUDE.md.

## Rozhodnutí

- **Parametr `p`, ne `page`:** `page` je v Razor Pages rezervovaná hodnota trasy (cesta stránky) – stejně jako výloha.
- **Stránkovat v databázi, kde data rostou** (objednávky, uživatelé, faktury, hodnocení, přihlášení); v paměti jen u malých
  seznamů nebo dat z API (produkty – API skladu se kvůli tomu nemění).
- **Velikost jen z nabídky** (10/20/50/100) – `?size=100000` by obešlo stránkování.
- **GET odkazy, žádný JavaScript pro listování** – funguje bez skriptů, CSP beze změny; skript jen pohodlně odešle výběr velikosti.
- **Bez stránkování pracovních front** (Expedice, Vratky) – mají se vyprázdnit a hromadné akce potřebují vidět vše.
- **Bez migrace** – řadí se podle primárního klíče a existujících indexů (e-mail v Identity je indexovaný).

## Známá omezení

- `COUNT` + `OFFSET` je pro stovky až desetitisíce záznamů v pořádku; při milionech by bylo lepší stránkování podle klíče („keyset“).
- Hledání v e-mailu je `LIKE '%…%'` (index nepomůže) – pro tisíce účtů stačí.
- Velikost stránky se nepamatuje – je v adrese (odkaz s ní jde poslat), po odchodu ze stránky se vrátí výchozí 20.
- Detail uživatele dál ukazuje všechny jeho objednávky (zákazník jich má jednotky); stránkuje se jen historie přihlášení.

## Jak ověřit

- `dotnet test` – `PagingTests` (oprava parametrů, okno odkazů, adresy, Uživatelé s hledáním a stránkami, Objednávky s hledáním
  a cizí objednávkou).
- Ručně: Správa → Uživatelé, *Na stránku* 10, druhá stránka; hledání části e-mailu; Objednávky → hledání podle čísla.

## Vyzkoušejte sami

1. Na Objednávkách zvolte *Na stránku* 10 a přejděte na 2. stránku – adresa je `?p=2&size=10`, jde poslat odkazem.
2. Zadejte do adresy `?p=999` – ukáže se poslední stránka, ne prázdný seznam.
3. Ve Správě → Hodnocení skryjte hodnocení na 2. stránce – stránka zůstane na 2.
