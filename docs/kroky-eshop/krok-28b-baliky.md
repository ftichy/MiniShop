# E-shop krok 28B: Balíky v objednávce (směr C, krok C2)

Plán: [Sklady-roadmap](../Sklady-roadmap.md). Sklad od kroku 28A ví, odkud zboží bere – teď to oznámí i Orderingu.
Objednávka má **balíky** (jeden za každý sklad) a Expedice je ukáže s filtrem „můj sklad“. Předání dopravci zůstává za
celou objednávku – vlastní zásilky po balících přijdou v kroku 28C.

## Jak to funguje

```
ReserveStockV1 → sklad (ReserveStockHandler, přidělení 28A)
   → StockReservedV1                     → sága (potvrzení objednávky – beze změny)
   → StockReservedV2 { Parcels: [BRQ: 7×3] [PRG: 7×1] }   (expand: nová zpráva vedle staré)
        → Ordering OrderParcelsHandler → Order.SetParcels (jen jednou)
             → OrderParcels: 1 = Brno (3 ks), 2 = Praha (1 ks)   (čísla podle kódu skladu)

Expedice /Admin/Fulfilment?warehouse=BRQ → jen objednávky s balíkem z Brna; štítky „Balík 1: Brno · 3 ks“
Objednávka bez balíků (starší) → Order.EffectiveParcels = jeden balík z Prahy se všemi položkami
```

Teorie:

- **Expand → contract zprávy:** `StockReservedV2` jde **vedle** `StockReservedV1`. Na V1 dál čeká sága, V2 nese navíc rozdělení.
  Až V2 budou poslouchat všichni, kteří potřebují, V1 půjde pryč (krok 28D).
- **Event-carried state transfer:** Ordering sklady nezná – kód a název skladu dostane jako text ve zprávě (jako název
  produktu v položce objednávky) a nemusí se skladu ptát.
- **Hranice agregátu:** balík je část objednávky. Pravidla, která přijdou v kroku 28C (odesláno a doručeno až po všech balících),
  bude hlídat jeden agregát a jedna transakce.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Inventory/StockReservedV2.cs` | nová událost + `StockParcelV1` (kód a název skladu, položky) |
| `src/MiniShop.Domain/Orders/OrderParcel.cs` | balík objednávky, `OrderParcelLine`, vstup `ParcelPlan` |
| `src/MiniShop.Application/Ordering/IntegrationEvents/OrderParcelsHandler.cs` | `StockReservedV2` → `Order.SetParcels` |
| `src/MiniShop.Infrastructure/Ordering/Migrations/…_OrderParcels.cs` | tabulky `OrderParcels`, `OrderParcelLines` – jen přidání |
| `src/MiniShop.Web/Pages/Admin/Fulfilment/_Parcels.cshtml` | štítky balíků u objednávky v Expedici |
| `tests/MiniShop.Tests/Domain/OrderParcelTests.cs` | výchozí balík z Prahy, číslování, jen jednou |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Application/Inventory/ReserveStockHandler.cs` | vedle V1 i V2 s balíky (i v idempotentní větvi – z uložených rezervací) |
| `src/MiniShop.Domain/Orders/Order.cs` | `Parcels`, `SetParcels`, `EffectiveParcels` |
| `src/MiniShop.Infrastructure/Ordering/Configurations/OrderConfiguration.cs` | balíky jako vlastní tabulka (auto include) |
| `src/MiniShop.Application/Ordering/OrderContracts.cs`, `OrderMapping.cs` | `OrderDto.Parcels` (`OrderParcelDto` s názvy produktů) |
| `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs` | registrace `OrderParcelsHandler` |
| `src/MiniShop.Web/Pages/Admin/Fulfilment/Index.cshtml(.cs)` | filtr Sklad (`?warehouse=`, akce ho zachovají), štítky balíků ve všech frontách |
| testy: `OrderServiceTests.Warehouses.cs`, `FulfilmentPageTests.cs`, `IntegrationContractTests.cs` | balíky celou cestou, filtr Expedice, tvar JSONu |
| příručka: `moduly/ordering.md`, `moduly/inventory.md`, `eda/observabilita.md`, `historie.md` | balíky, nová zpráva, atribut `minishop.order.parcels` |

## Rozhodnutí

- **Číslování balíků podle kódu skladu** (BRQ = 1, PRG = 2) – stejná data dají vždy stejná čísla, i kdyby zpráva přišla znovu.
- **Bez balíků = jeden balík z Prahy** – pravda pro všechny objednávky z doby před sklady (krok 28A přepsal zásoby do Prahy).
- **Filtr skladu bez nových rolí** (rozhodnutí 22. 11. 2026). Seznam skladů ve filtru = sklady z balíků ve frontách + Praha –
  Ordering si seznam skladů nedrží.
- **Zákazník balíky zatím nevidí** – ukážou se až s vlastními zásilkami (krok 28C).

## Známá omezení

- Při postupném nasazení vymění `deploy.sh` sklad dřív než Web. `StockReservedV2` z nového skladu může vyzvednout stará kopie Webu
  bez handleru a zahodit ji – taková objednávka pak zobrazí „jeden balík z Prahy“, i když je z Brna (týká se jen rozdělených
  objednávek z těch pár desítek sekund).
- Expedice pořád předává a odesílá **celou objednávku** – štítky balíků jsou jen informace, odkud se balí.

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `OrderParcelTests`, `Sklady_*`, `FulfilmentPageTests.FiltrSkladu_*`.
2. Po nasazení objednat produkt víc kusů, než má Praha (část v Brně), a na *Správa → Expedice* najít štítky
   `Balík 1: Brno · …` a `Balík 2: Praha · …`; filtr Sklad = Brno ukáže jen objednávky s balíkem z Brna.
3. Starší objednávky ve frontách mají štítek `Praha · n ks`.

## Vyzkoušejte sami

- Objednejte dva produkty – jeden jen v Praze, druhý jen v Brně: objednávka má dva balíky, každý s jedním produktem
  (na trase `minishop.order.parcels` = 2).
- Ve filtru zvolte Brno, začněte objednávku balit – po akci zůstane filtr Brno.
