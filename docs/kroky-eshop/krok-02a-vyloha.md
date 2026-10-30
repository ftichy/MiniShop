# E-shop krok 2A: Výloha

MiniShop má veřejnou výlohu `/Shop`: karty produktů s popisem, kategorií a dostupností („Skladem“, „Poslední kusy“,
„Vyprodáno“), hledání bez ohledu na diakritiku, filtr kategorie a „jen skladem“, řazení a stránkování. Admin spravuje
kategorie a upravuje produkty. Celá výloha čte jen kopii katalogu v Orderingu – funguje i bez skladu.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md). Obrázky produktů jsou samostatný krok 2B.

## Jak to funguje

Dva EDA vzory a jeden CQRS:

**1. Nová verze kontraktu (expand → contract).** Popis a kategorie se do `ProductUpdatedV1` přidat nesmí – kontrakt se
po zveřejnění nemění. Sklad proto posílá **`ProductUpdatedV2`** (celý stav produktu) **a zatím i V1**. Přirovnání: úřad
vydá „vzor 2“ tiskopisu a nějakou dobu přijímá oba, protože lidé mají doma vyplněný starý.

```
 Product.Update / new Product / CategoryRenamed
   └─► ProductChanged ──InventoryDbContext.ToOutboxMessages──► ProductUpdatedV1  (ID = EventId)
                                                            └─► ProductUpdatedV2  (ID = odvozené z EventId)
                                                                   │
 Ordering: CatalogProjectionHandler ◄──────── RabbitMQ ◄──────────┘
   V1 → Apply(název, cena)            V2 → ApplyDetails(+ popis, kategorie) + ApplyAvailability(snímek)
```

Proč odvozené ID: inbox rozlišuje zprávy podle (ID, handler). V1 i V2 zpracuje tentýž `CatalogProjectionHandler`,
takže se stejným ID by V2 zahodil jako duplicitu. Náhodné ID nejde – při opakovaném uložení musí vyjít stejné.

**2. Událost podle potřeby příjemce.** Výloha nepotřebuje přesný počet kusů – jen pásmo (víc než 5 ks / 1–5 / 0).
`Product.AddStock`/`RemoveStock` vyvolá `StockAvailabilityChanged` **jen při změně pásma** → `StockAvailabilityChangedV1`.
Přirovnání: prodejna nepotřebuje inventurní soupis, stačí jí cedulka „poslední kusy“; sklad volá, jen když se má vyměnit.

```
 sága: ReserveStockV1 (5 ks) ─► ReserveStockHandler ─► Product.RemoveStock(5, čas)
                                  pásmo LowStock → OutOfStock ─► StockAvailabilityChanged
                                  COMMIT: zásoba + rezervace + Outbox[StockReservedV1, StockAvailabilityChangedV1]
 Ordering: CatalogProduct.ApplyAvailability(OutOfStock, čas) – jen když je novější ─► výloha „Vyprodáno“
```

Dostupnost má vlastní čas (`AvailabilityChangedAtUtc`) – změna ceny a vyprodání jsou nezávislé zprávy.

**3. Read model tvarovaný pro čtení (CQRS).** SQLite neumí hledat bez diakritiky („mys“ ≠ „Myš“). Kopie katalogu si
při každé změně uloží `SearchText` (název + popis + kategorie, malá písmena bez diakritiky) a stejně se upraví hledaný
text. Filtr dělá databáze, řazení a stránkování `CatalogQueryService` v paměti (SQLite neřadí `decimal`).

**Kategorie** patří skladu (entita `Category`). Ordering je zná jen ze zpráv o produktech – přejmenování proto sklad
oznámí novou `ProductUpdatedV2` pro každý produkt kategorie.

**Po nasazení** mají produkty v kopii katalogu dostupnost `Unknown` (migrace). `CatalogSyncOnStartupWorker` teď
obnovuje kopii nejen prázdnou, ale i neúplnou – Web si po startu sám řekne o celý katalog (`SyncCatalogV1`).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Inventory/ProductUpdatedV2.cs` | Celý stav produktu pro výlohu (popis, kategorie, jméno obrázku, dostupnost + její čas) |
| `src/MiniShop.Contracts/Inventory/StockAvailabilityChangedV1.cs` | Změna pásma dostupnosti; hodnoty `StockAvailabilityV1` (text, ne enum) |
| `src/MiniShop.Domain/Inventory/Category.cs`, `StockAvailability.cs`, `Events/StockAvailabilityChanged.cs` | Kategorie, pásma dostupnosti (`StockAvailabilityBands.LowStockLimit` = 5), doménová událost |
| `src/MiniShop.Domain/Orders/CatalogAvailability.cs`, `CatalogSearchText.cs` | Dostupnost v kopii katalogu (`Unknown` …), normalizace textu pro hledání |
| `src/MiniShop.Application/Inventory/CategoryService.cs`, `ICategoryService.cs`, `ICategoryRepository.cs`, `InventoryContractMapping.cs` | Správa kategorií; doména skladu → kontrakty (sdílí DbContext i obnova katalogu) |
| `src/MiniShop.Application/Ordering/Catalog/CatalogQueryService.cs`, `ICatalogQueryService.cs`, `CatalogSearchContracts.cs` | Výloha: hledání, filtry, řazení, stránkování (12 na stránku) |
| `src/MiniShop.Infrastructure/Outbox/MessageIds.cs` | Odvozené ID zprávy (V2 vedle V1) |
| `src/MiniShop.Infrastructure/Inventory/CategoryRepository.cs`, `Configurations/CategoryConfiguration.cs` | Kategorie v `inventory.db` |
| `src/MiniShop.Infrastructure/Inventory/Migrations/…_CategoriesAndAvailability.cs` | Tabulka `Categories` + „Nezařazeno“ (ID 1) pro dosavadní produkty, `AvailabilityChangedAtUtc` = `ChangedAtUtc` (ručně upraveno) |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_CatalogShopWindow.cs` | Popis, kategorie, dostupnost (`Unknown`), `SearchText` (zatím `lower(Name)`) – ručně upraveno |
| `src/MiniShop.Inventory.Api/Controllers/CategoriesController.cs` | `GET/POST /api/categories`, `PUT/DELETE /api/categories/{id}` |
| `src/MiniShop.Web/Pages/Shop/Index.cshtml(.cs)` | Výloha (veřejná, filtry v URL: `q`, `category`, `instock`, `sort`, `p`) |
| `src/MiniShop.Web/Pages/Products/Edit.cshtml(.cs)` | Úprava produktu (název, popis, cena, kategorie) |
| `src/MiniShop.Web/Catalog/AvailabilityLabels.cs` | Texty a barvy dostupnosti pro výlohu i formulář |
| `docs/prirucka/toky/zmena-dostupnosti.md` | Nový sekvenční diagram |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Inventory/Product.cs` | Kategorie, pásmo dostupnosti + čas jeho změny, `CategoryRenamed`; `AddStock`/`RemoveStock` dostávají čas |
| `Domain/Orders/CatalogProduct.cs` | Popis, kategorie, obrázek, dostupnost, `SearchText`; `ApplyDetails`, `ApplyAvailability` |
| `Infrastructure/Persistence/ModuleDbContext.cs` | `ToOutboxMessages` – jedna událost může dát víc zpráv |
| `Infrastructure/Inventory/InventoryDbContext.cs` | `ProductChanged` → V1 + V2, `StockAvailabilityChanged` → `StockAvailabilityChangedV1` |
| `Infrastructure/Inventory/ProductRepository.cs`, `InventoryDatabaseInitializer.cs` | Produkt vždy s kategorií; seed se 4 kategoriemi |
| `Application/Inventory/ProductService.cs`, `ProductContracts.cs`, `ReserveStockHandler.cs`, `ReleaseStockHandler.cs`, `SyncCatalogHandler.cs` | Kategorie v DTO a požadavcích; čas změny zásoby; obnova posílá i V2 |
| `Application/Ordering/Catalog/CatalogProjectionHandler.cs`, `CatalogSyncService.cs` | V2 a dostupnost; obnova i neúplné kopie (`RequestIfEmptyOrIncompleteAsync`) |
| `Application/Ordering/OrderContracts.cs`, `OrderMapping.cs`, `ICatalogProductRepository.cs` | `CatalogItemDto` s popisem, kategorií a dostupností; `SearchAsync`, `AnyIncompleteAsync` |
| `Web/Inventory/InventoryApiClient.cs`, `Extensions/ApplicationServiceCollectionExtensions.cs` | Klient i pro kategorie (jeden typed client, dvě rozhraní); registrace V2, dostupnosti, výlohy |
| `Web/Pages/Orders/Index.cshtml(.cs)` | Dostupnost z kopie katalogu (žádné HTTP na sklad), předvýběr `?productId=` z výlohy |
| `Web/Pages/Products/Index.cshtml(.cs)` | Sloupec a výběr kategorie, správa kategorií, odkaz na úpravu |
| `Web/Pages/Shared/_Layout.cshtml`, `Pages/Index.cshtml` | Menu „Obchod“, odkaz na výlohu |
| testy | `ProductTests`, `CatalogProductTests`, `OrderServiceTests` (V2, pásma přes ságu, přejmenování kategorie, neúplná kopie, hledání, stránkování), `IntegrationContractTests`, `DatabaseMigrationTests` (stará DB = schéma první migrace, ruční úpravy migrací), `AuthorizationMatrixTests` |
| `docs/prirucka/…` | Kopie katalogu (výloha, CQRS), integrační zprávy (expand → contract), toky, moduly Inventory a Ordering, kontejnery, slovníček, historie |

## Rozhodnutí v tomto kroku

- **V2 jako „tlustá“ událost s celým stavem**, včetně dostupnosti jako snímku – nový produkt i obnova katalogu dostanou
  vše jednou zprávou. `StockAvailabilityChangedV1` pro produkt, který kopie ještě nezná, se proto může zahodit.
- **V2 navržená rovnou s `ImageFileName`** (zatím vždy `null`) – obrázky v kroku 2B nebudou potřebovat V3.
- **Expand → contract**: sklad posílá V1 i V2, Ordering zpracuje obě. Odebrání V1 je samostatný malý krok.
- **Pásma místo čísla** (hranice 5 ks v `StockAvailabilityBands`) – méně zpráv, výloha nezná, co jí nepatří.
- **Kategorie bez vlastní zprávy** – jméno jde ve zprávě o produktu, přejmenování oznámí produkty. Alternativa
  `CategoryUpdatedV1` + kopie kategorií v Orderingu by byla čistší, ale pro pár kategorií zbytečně složitá.
- **Kategorie povinná**; dosavadní produkty dala migrace do „Nezařazeno“ (pevné ID 1). Smazat jde jen prázdnou.
- **Unikátnost a řazení názvů česky v paměti** – SQLite umí bez rozlišení velikosti písmen jen ASCII.
- **`SearchText` v doméně** (`CatalogSearchText`) – je to pravidlo read modelu, ne SQL; migrace do první V2 aspoň `lower(Name)`.
- **Filtr v SQL, řazení v paměti** – SQLite neřadí `decimal`; pro stovky produktů zanedbatelné.
- **Filtry v URL, formulář GET, bez JavaScriptu** – odkaz jde sdílet, funguje Zpět, žádná výjimka v CSP. Číslo stránky
  je `p`, protože `page` je v Razor Pages vyhrazená hodnota cesty.
- **Formulář objednávky bez HTTP na sklad** – dostupnost z kopie; zmizela hláška „sklad neodpovídá“.
- **Jeden `InventoryApiClient` pro `IProductService` i `ICategoryService`** – stránky berou jen rozhraní, které potřebují (ISP).

## Oprava po nasazení: nešlo přidat kategorii

V produkci formulář kategorie vždy „neprošel validací“. Příčina: stránka Produkty má `[BindProperty] NewProduct` a formulář
kategorie jeho pole neposílá. ASP.NET Core pak `NewProduct` naváže **bez prefixu** – jeho chyby mají klíče `Name`, `Price`,
`CategoryId`, ne `NewProduct.Name`, takže je původní kód (mazal jen klíče začínající `NewProduct`) nepoznal.
Oprava: `IsCategoryValid` smaže všechny chyby a znovu ověří jen kategorii (`TryValidateModel(category, "category")`).
Testy oprávnění posílaly formulář prázdný, proto to nezachytily – nové `ProductAdministrationTests` posílají
`category.Name` jako prohlížeč.

## Známá omezení

- Sklad posílá každou změnu produktu dvakrát (V1 + V2), dokud se V1 neodebere.
- Výloha může být pár sekund pozadu; „Skladem“ ještě neznamená úspěšnou rezervaci.
- Hledání podřetězcem bez indexu a bez tolerance překlepů (pro tisíce produktů FTS5 nebo vyhledávací služba).
- Kategorie jsou ploché (bez podkategorií) a bez vlastního pořadí – řadí se abecedně.
- Bez obrázků (krok 2B) a bez detailu produktu – karta ukazuje vše podstatné.
- Starý produkt ve výloze až do doběhnutí obnovy katalogu po nasazení (~2–4 s) bez kategorie a s „Dostupnost ověříme“.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu: `Applying migration '…_CategoriesAndAvailability'` (sklad) a `'…_CatalogShopWindow'` (Web), pak
   `[Katalog] Kopie katalogu je prázdná nebo neúplná – žádám sklad o celý katalog`. Data zůstala.
2. **Obchod** v menu (i nepřihlášený): karty s kategorií „Nezařazeno“ a dostupností. Hledej `MYS` – najde „Bezdrátová myš“.
3. Jako admin → **Produkty** → přidej kategorii „Periferie“, u myši **Upravit** → kategorie Periferie. Za pár sekund
   je ve výloze ve filtru kategorií.
4. Přejmenuj kategorii – výloha ukáže nové jméno (sklad poslal V2 pro každý produkt kategorie).
5. Jako zákazník u produktu s „Poslední kusy“ (např. monitor, 5 ks) → **Objednat** → formulář má produkt předvybraný →
   objednej všech 5 ks. Po potvrzení je ve výloze „Vyprodáno“ a tlačítko Objednat zmizí. Zruš objednávku → zpět „Poslední kusy“.
6. RabbitMQ UI (http://localhost:15672): exchange `ProductUpdatedV2` a `StockAvailabilityChangedV1` navázané na `minishop.ordering`.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup`.
2. Sloučit PR → CD nasadí obě služby. Migrace přidají sloupce a kategorii „Nezařazeno“, data zůstanou.
3. Po startu Webu se kopie katalogu sama doplní (obnova neúplné kopie). Ověření: https://minishop-tichy.duckdns.org/Shop
   ukazuje dostupnost; admin roztřídí produkty do kategorií.
4. Žádný nový secret ani změna `deploy.sh`/`backup.py`.

## Vyzkoušejte sami

- Naskladni produkt se 40 kusy o 10 ks a podívej se do outboxu skladu (`inventory.db`, `OutboxMessages`): žádná
  `StockAvailabilityChangedV1`. Pak ho vyprodej objednávkou – zpráva přibude.
- V outboxu skladu najdi dvojici `ProductUpdatedV1` a `ProductUpdatedV2` jedné změny – porovnej jejich `Id`.
- V `InventoryDbContext` dej V2 stejné ID jako V1 a spusť `OrderServiceTests`. Které testy spadnou a proč?
- Změň `StockAvailabilityBands.LowStockLimit` na 3 – kolik testů to zachytí?
- Hledej `audio` – najde i produkty kategorie Audio (kategorie je součástí `SearchText`).
