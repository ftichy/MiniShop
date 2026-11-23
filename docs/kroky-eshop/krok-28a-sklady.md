# E-shop krok 28A: Víc skladů (směr C, krok C1)

Plán: [Sklady-roadmap](../Sklady-roadmap.md). E-shop má dva sklady – Praha (dnešní zásoby) a Brno (prázdný). Sklad ví, kolik
čeho je kde, rezervace bere zboží z jednoho skladu, když to jde, jinak ho rozdělí. **Zákazník ani Ordering nic nepoznají** –
výloha ukazuje součet, objednávka se potvrdí stejně. Balíky v objednávce přijdou až v kroku 28B.

## Jak to funguje

```
Produkt (agregát) ── StockQuantity = součet (výloha, pásma dostupnosti, zprávy beze změny)
   └─ WarehouseStocks: Praha 2 ks · Brno 3 ks

ReserveStockV1 (4 ks) → ReserveStockHandler
   1. stačí celková zásoba?            ne → StockReservationFailedV1 (jako dřív)
   2. StockAllocator.Allocate:
        jeden sklad má všechno?         → celé odtud (méně balíků)
        jinak sklad, který pokryje nejvíc zbývajících kusů (shoda = nižší ID) … Brno 3, Praha 1
   3. Product.RemoveStock(sklad, kusy) + StockReservation(…, WarehouseId)   → StockReservedV1 (jako dřív)

ReleaseStockV1 / vratka / vráceno dopravcem → Product.AddStock(reservation.WarehouseId, …) = zpět do skladu původu
```

Teorie:

- **Přidělení zásob (sourcing)** patří tomu, kdo zná zásoby – skladu. Ordering se v dalším kroku dozví jen výsledek (balíky).
- **Hranice agregátu:** zásoba ve skladu je část agregátu `Product`, ne samostatný agregát. Pravidlo „celková zásoba = součet
  skladů“ i „ze skladu nejde vzít víc, než má“ hlídá jedno místo.
- **Expand bez změny kontraktů:** žádná nová zpráva, chování navenek stejné. Nové je jen to, co sklad ví o sobě.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Inventory/Warehouse.cs` | `Warehouse` (Praha = `DefaultId`, Brno) a `WarehouseStock` (zásoba produktu v jednom skladu) |
| `src/MiniShop.Domain/Inventory/StockAllocator.cs` | přidělení zásob – jeden sklad, když to jde, jinak co nejméně skladů |
| `src/MiniShop.Infrastructure/Inventory/Configurations/WarehouseConfiguration.cs` | tabulka `Warehouses` + oba sklady (`HasData`) |
| `src/MiniShop.Infrastructure/Inventory/Migrations/…_Warehouses.cs` | tabulky `Warehouses`, `WarehouseStocks`, sloupec `StockReservations.WarehouseId` (výchozí 1); dnešní zásoby → Praha (ruční SQL) |
| `src/MiniShop.Inventory.Api/Controllers/WarehousesController.cs` | `GET /api/warehouses` |
| `tests/MiniShop.Tests/Domain/StockAllocatorTests.cs` | jeden sklad, shoda, rozdělení, nedostatek, vrácení, rozdíl ze starší verze |
| `tests/MiniShop.Tests/Application/OrderServiceTests.Warehouses.cs` | rezervace ze dvou skladů celou cestou, zrušení vrátí kusy do skladů původu |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Domain/Inventory/Product.cs` | `Stocks`, `StockIn(sklad)`, `AddStock`/`RemoveStock` se skladem; rozdíl ze starší verze patří Praze |
| `src/MiniShop.Domain/Inventory/StockReservation.cs` | `WarehouseId` |
| `src/MiniShop.Application/Inventory/ReserveStockHandler.cs` | rezervace podle `StockAllocator`, atribut `minishop.stock.warehouses` |
| `ReleaseStockHandler.cs`, `RestockReturnedGoodsHandler.cs` | kusy zpět do skladu původu |
| `ProductService.cs`, `IProductService.cs`, `IProductRepository.cs`, `ProductContracts.cs`, `ProductMapping.cs` | naskladnění do skladu, seznam skladů, `ProductDto.Stocks` |
| `src/MiniShop.Infrastructure/Inventory/…` | `WarehouseStocks` jako vlastní tabulka produktu, `DbSet<Warehouse>`, výchozí sklad rezervace |
| `src/MiniShop.Inventory.Api/Controllers/ProductsController.cs`, `Web/Inventory/InventoryApiClient.cs` | `WarehouseId` při naskladnění, `/api/warehouses` |
| `src/MiniShop.Web/Pages/Products/Index.cshtml(.cs)` | zásoba po skladech (`PRG 2 · BRQ 3`) a volba skladu u naskladnění |
| příručka: `moduly/inventory.md`, `eda/observabilita.md`, `historie.md`; `docs/Sklady-roadmap.md` | popis skladů, nový atribut, plán směru |

## Rozhodnutí

- **Dva pevné sklady z migrace**, bez správy skladů v aplikaci – pro výuku stačí.
- **Jeden sklad, když to jde**; jinak „nejvíc zbývajících kusů“ (hladový algoritmus – pro dva sklady vždy optimální, pro víc
  skladů dobrý odhad). Při shodě nižší ID = Praha.
- **`StockQuantity` zůstává uložený součet** – výloha, pásma i zprávy se nemusely měnit a starší verze skladu při postupném
  nasazení dál funguje. Rozdíl, který způsobí, patří Praze (`Product.StockIn`) a zapíše se při další změně.
- **Naskladnění bez skladu = Praha** – tak volá i starší verze Webu (`AddStockRequest.WarehouseId` chybí).

## Známá omezení

- Ordering, Expedice ani zákazník sklady zatím nevidí – rezervace z obou skladů se vyřizuje jako jedna zásilka (krok 28B/28C).
- Import produktů (`ProductImportService`) zakládá zásobu jen v Praze.
- Rozdíl ze starší verze se připíše Praze, i kdyby starší verze vydala zboží z Brna – v kroku 28A ale Brno při nasazení
  ještě nic nemá.

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `StockAllocatorTests`, `Sklady_*`, `DatabaseMigrationTests`.
2. Po nasazení *Správa → Produkty*: sloupec Skladem ukazuje `PRG n · BRQ 0`, u naskladnění je volba skladu.
3. Naskladnit do Brna a objednat víc kusů, než má Praha – objednávka se potvrdí. V tabulce `inventory."StockReservations"`
   jsou dva řádky s různým `WarehouseId`, po zrušení se kusy vrátí do obou skladů.

## Vyzkoušejte sami

- Naskladněte produkt jen do Brna a objednejte ho: rezervace vezme všechno z Brna (jeden sklad má všechno), Praha zůstane.
- Objednejte dva různé produkty, z nichž jeden je jen v Praze a druhý jen v Brně: rezervace má dva sklady
  (na trase atribut `minishop.stock.warehouses` = 2).
