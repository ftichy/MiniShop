# E-shop krok 21: Lepší hledání ve výloze

Cíl: hledání rozumí víc slovům v libovolném pořadí, překlepům, řadí podle toho, jak dobře produkt odpovídá, a během psaní
našeptává. S 30 produkty z issue #115 už dřívější hledání jednoho souvislého textu nestačilo („notebook 32 gb“ nenašlo nic).

## Jak to funguje

Přirovnání: dobrý prodavač – když řeknete „ten notebook s 32 giga“, nehledá přesně tuhle větu na krabici, a když řeknete
„klávesnyce“, ví, co myslíte. Napřed ale nabídne to, co sedí přesně.

1. **Slova:** `CatalogSearchText.Words` dotaz normalizuje (malá písmena bez diakritiky), rozdělí na slova (max. 8) a ořízne
   interpunkci na krajích. Repozitář přidá za každé slovo `"SearchText" LIKE '%slovo%'` (AND) – pořadí slov nehraje roli
   a stačí kus slova. `%`, `_` a `\` z dotazu se escapují.
2. **Relevance:** s hledaným textem je výchozí řazení `CatalogSort.Relevance` (`CatalogRelevance.Score`: název ×3, kategorie ×2,
   popis ×1; celé slovo 3, začátek slova 2, kus slova 1; +1, když název začíná prvním slovem). Bez textu se dál řadí podle názvu.
3. **Překlepy:** když přesné hledání nic nenajde, `SearchSimilarAsync` pustí každé slovo, které je v textu přesně **nebo**
   má `word_similarity(slovo, SearchText) ≥ 0,4` (PostgreSQL rozšíření pg_trgm – trojice písmen); řadí podle podobnosti celého
   dotazu, nejvýš 48 výsledků. Stránka ukáže „přesně jsme nic nenašli, zobrazujeme podobné výsledky“.
4. **Našeptávač:** `GET /api/catalog/suggest?q=` (veřejný, od 2 znaků, limit 120/min z IP) vrátí až 3 kategorie, jejichž název
   začíná hledanými slovy, a 6 nejlepších produktů. `search-suggest.js` ho ukáže pod polem hledání v hlavičce i na stránce Obchod.
5. **Trasa:** `minishop.search.results` a `minishop.search.similar` – text dotazu ne (volný text zákazníka).

## Nové soubory

- `src/MiniShop.Application/Ordering/Catalog/CatalogRelevance.cs` – body relevance
- `src/MiniShop.Infrastructure/Ordering/Migrations/…_CatalogSearchTrigrams.cs` – `CREATE EXTENSION IF NOT EXISTS pg_trgm` + index GIN
- `src/MiniShop.Web/Controllers/CatalogController.cs` – `/api/catalog/suggest`
- `src/MiniShop.Web/Security/CatalogRateLimiting.cs` – limit našeptávače (`RateLimiting:CatalogSuggest`)
- `src/MiniShop.Web/wwwroot/js/search-suggest.js` – našeptávač (combobox + listbox, jen `textContent`)

## Změněné soubory

`CatalogSearchText` (`Words`), `CatalogSearchContracts` (`CatalogSort.Relevance`, `Sort` nullable, `CatalogSearchResult.Sort`
+ `IsSimilarMatch`, `CatalogSuggestions`), `ICatalogQueryService`/`CatalogQueryService` (`SuggestAsync`, záložní podobné hledání),
`ICatalogProductRepository`/`CatalogProductRepository` (slova, `SearchSimilarAsync`), `OrderingDbContext` (`HasPostgresExtension`),
`CatalogProductConfiguration` (index), `Pages/Shop/Index` (relevance v řazení, hláška o podobných výsledcích, našeptávač),
`_Layout` (našeptávač v hlavičce, skript), `site.css`, `Program.cs`, testy (`OrderServiceTests`, `AuthorizationMatrixTests`,
`MiniShopWebFactory`), příručka (`eda/kopie-katalogu.md`, `eda/observabilita.md`, `architektura/moduly/ordering.md`,
`nasazeni/grafana-tutorial.md`, `historie.md`), CLAUDE.md.

## Rozhodnutí

- **pg_trgm, ne fulltext `tsvector`:** fulltext PostgreSQL potřebuje pro češtinu slovník (není v instalaci) a překlepy neřeší;
  trojice písmen fungují pro jakýkoli jazyk a i pro názvy modelů („nordix“, „rtx“). Rozšíření je od PostgreSQL 13 „trusted“.
- **Podobná slova jen jako záloha:** přesná shoda má vždy přednost – jinak by „mys“ přimíchalo „mic“ nebo „sit“.
- **Relevance v paměti, ne v SQL:** stejně jako ostatní řazení (stovky produktů); pravidla jsou čitelná a testovatelná.
- **Rozšíření bez schématu v modelu:** `HasPostgresExtension("public", …)` Npgsql do snímku modelu zapíše bez schématu a hlásí
  „pending model changes“; bez schématu ho PostgreSQL založí v `public` (první existující schéma v `search_path`).
- **Escape znak u `LIKE` výslovně:** `EF.Functions.Like` bez něj Npgsql přeloží s `ESCAPE ''` a `\%` by neznamenalo nic.
- **Řazení `Sort` nullable:** „nic nevybráno“ = výchozí podle situace; výslovně zvolené řazení (i „podle názvu“) má přednost.
- **Kontrakty, sklad, Journeys beze změny**, žádný nový secret. Migrace jen přidává (expand) – starší verze kódu s ní běží.

## Oprava po nasazení: klik na návrh

V produkci se po kliknutí na návrh produkt neotevřel. Pole hledání při kliknutí ztratilo fokus a skript nabídku po 150 ms skryl,
dřív než klik doběhl (delší stisk, telefon). Oprava: `mousedown` v nabídce dělá `preventDefault`, takže pole fokus neztratí.
Nabídka se zavírá až skutečným odchodem z pole. Hlídá to E2E test `HledaniTests` (klik s delším stiskem na počítači, klepnutí
na telefonu) – unit testy chování prohlížeče nezachytí.

## Známá omezení

- Podobnost trojic nepozná přehozená písmena v krátkých slovech („mši“ ≠ „myš“) a velmi zkomolená slova (< 0,4).
- Kus slova najde i nečekané shody („gb“ v „gbps“); relevance je dá níž, ale nevyřadí.
- Kategorie v našeptávači jen podle začátku slov názvu, bez překlepů.
- Limit našeptávače je v paměti každé kopie Webu (2 kopie = reálně až 240/min).

## Jak ověřit

- `dotnet test` – `OrderServiceTests.Hledani_*` (víc slov, začátek slova, diakritika, relevance, překlepy, filtr „jen skladem“,
  znaky `%`/`_`), `Naseptavac_KategorieAProdukty`, `AuthorizationMatrixTests` (našeptávač veřejný).
- Ručně: do hledání v hlavičce napište „note“ → našeptávač ukáže kategorii Notebooky a notebooky; „klavesnyce“ → podobné výsledky.

## Vyzkoušejte sami

1. Hledejte „32 gb notebook“ – najde se notebook, i když slova v popisu jsou jinak.
2. Hledejte „monitr“ – nahoře je žlutá hláška o podobných výsledcích a pod ní monitory.
3. V našeptávači projděte návrhy šipkami a Enterem otevřete produkt; Esc nabídku zavře.
4. V Grafaně (Explore → Tempo): `{ span.minishop.search.results = 0 }` – hledání, která nic nenašla (co v nabídce chybí).
