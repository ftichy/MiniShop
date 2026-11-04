# E-shop krok 14: Hodnocení produktů

Zákazníci hodnotí produkty (1–5 hvězd a text) na nové stránce detailu produktu `/Shop/{id}`. Hodnotit smí jen ten,
kdo produkt **koupil a dostal**. Nový modul **Reviews** si to ověřuje nad **vlastní kopií nákupů** z událostí
objednávek, na Ordering se neptá. Průměr putuje opačným směrem: Reviews oznámí `ProductRatingChangedV1` a výloha
ukáže hvězdičky z kopie katalogu. Volitelný námět z [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na
[krok 13](krok-13-slevove-kody.md).

## Jak to funguje

Přirovnání: recenzi píše jen ten, kdo má účtenku – a prodejna si kopie účtenek drží sama.

```
 Ordering ─ OrderPlacedV2, OrderCompletedV1, ReturnCompletedV1 ─► Reviews: PurchasedOrder (kdo, co, doručeno, vráceno)
 zákazník ─ /Shop/{id}: hodnocení ─► smí? (doručeno, nevráceno) ─► COMMIT Review + Outbox[ProductRatingChangedV1]
 Reviews ─ ProductRatingChangedV1 ─► Ordering: CatalogProduct.ApplyRating ─► výloha: hvězdičky, řazení „nejlépe hodnocené“
 start s prázdnou kopií ─ SyncPurchasesV1 ─► Ordering ─ OrderSnapshotV2 × N ─► Reviews (nákupy z doby před modulem)
 CustomerAnonymizedV1 ─► kopie nákupů smazaná, autor „Smazaný zákazník“
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Event-carried state transfer** | odběratel si drží kopii dat ze zpráv, místo aby se ptal | `PurchasedOrder` z `OrderPlacedV2`/`OrderCompletedV1` |
| **Projekce do cizího modulu** | souhrn z jednoho modulu se stane částí čtecího modelu druhého | `ProductRatingChangedV1` → `CatalogProduct.RatingAverage` |
| **Nová verze kontraktu** | potřebuji víc dat → V2, odběratelé V1 se nemění | `OrderSnapshotV2` (se zákazníkem) vedle `OrderSnapshotV1` |
| **Doplnění historie novému odběrateli** | nový modul si vyžádá snímky stavu | `SyncPurchasesV1` → `OrderSnapshotV2` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Reviews/ProductRatingChangedV1.cs`, `SyncPurchasesV1.cs`, `Orders/OrderSnapshotV2.cs` | souhrn hodnocení, žádost o nákupy, snímek se zákazníkem |
| `src/MiniShop.Domain/Reviews/*` | `Review`, `PurchasedOrder` |
| `src/MiniShop.Application/Reviews/*` | `ReviewService`, `PurchaseProjectionHandler`, rozhraní modulu |
| `src/MiniShop.Application/Ordering/IntegrationEvents/SyncPurchasesHandler.cs` | Ordering pošle snímky nákupů |
| `src/MiniShop.Infrastructure/Reviews/*` | `ReviewsDbContext` (inbox + outbox), repozitáře, migrace `InitialReviews` |
| `src/MiniShop.Web/Pages/Shop/Detail.cshtml(.cs)`, `Pages/Admin/Reviews/*`, `Catalog/RatingStars.cs`, `BackgroundServices/PurchasesSyncOnStartupWorker.cs` | detail produktu, moderace, hvězdičky, doplnění nákupů po startu |
| `docs/prirucka/architektura/moduly/reviews.md`, `toky/hodnoceni-produktu.md`, tento souhrn | dokumentace |
| `tests/…/Domain/ReviewTests.cs` | pravidla hodnocení a kopie nákupů |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/CatalogProduct.cs`, `CatalogProductConfiguration.cs` | `RatingAverage`, `RatingCount`, `ApplyRating`; migrace `CatalogRatings` (ordering.db) |
| `Application/Ordering/Catalog/*`, `OrderContracts.cs`, `OrderMapping.cs` | `ProductRatingChangedV1` v projekci, `CatalogSort.BestRated`, `GetProductAsync`, hodnocení v `CatalogItemDto` |
| `Application/IntegrationModules.cs` | `Reviews` (fronta `minishop.reviews`) |
| `Web/Program.cs`, `Extensions/…`, `appsettings.json`, `Dockerfile`, `deploy/backup.py` | `reviews.db`, registrace, worker, záloha |
| `Web/Pages/Shop/Index.cshtml`, `_Layout.cshtml`, `Privacy.cshtml` | odkaz na detail, hvězdičky, řazení, menu **Hodnocení**, osobní údaje |
| příručka (`moduly/index`, `moduly/ordering`, `toky/index`, `historie`) | dokumentace |
| `tests/…/OrderServiceTests.cs`, `AuthorizationMatrixTests.cs`, `ModuleBoundariesTests.cs`, `MiniShopWebFactory.cs` | modul v testech, celé cesty, oprávnění, hranice |

## Rozhodnutí v tomto kroku

- **Samostatný modul s kopií nákupů** – Reviews nezávisí na dostupnosti Orderingu a moduly se neznají.
- **Souhrn ve stejné transakci** jako hodnocení (outbox) a v Orderingu „starší nepřepíše novější“ (`RatingChangedAtUtc`).
- **Nákupy z doby před modulem** přes `SyncPurchasesV1` → `OrderSnapshotV2` (automaticky nad prázdnou DB);
  anonymizované objednávky se neposílají.
- **Autor jen „Jan N.“**, po smazání účtu „Smazaný zákazník“; hodnocení zůstává (pomáhá ostatním).
- **Jedno hodnocení na zákazníka a produkt**, druhé odeslání = úprava; vrácené zboží už hodnotit nejde.
- **`reviews.db` musí do zálohy** – na rozdíl od přehledu tržeb ji nejde přestavět (hodnocení jsou jen tady).

## Známá omezení

- Admin hodnocení jen skryje, nesmaže; zákazník své hodnocení smazat nemůže (jen upravit).
- Hodnocení bez fotek, bez odpovědí obchodu a bez „bylo užitečné“.
- Stránka Hodnocení ukazuje číslo produktu, ne název (název zná Ordering – stránka moduly nespojuje).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu migrace `…_InitialReviews`, `…_CatalogRatings` a „Kopie nákupů je prázdná – žádám Ordering o snímky“.
2. Výloha → klik na název produktu → detail s „zatím bez hodnocení“.
3. Zákazník s doručenou objednávkou → detail produktu → formulář → 4 hvězdy → „Děkujeme…“.
4. Za pár sekund ve výloze ★★★★☆ (1) a řazení „nejlépe hodnocené“.
5. Zákazník bez doručené objednávky formulář nevidí („Hodnotit můžete zboží, které jste koupili a dostali“).
6. Admin → **Hodnocení** → Skrýt → hvězdičky ve výloze zmizí.

### Produkce

Nic nového k nastavení – `reviews.db` vznikne sama, po startu se doplní nákupy ze všech objednávek.

## Vyzkoušejte sami

- Proč `PurchasedOrder` neumí doručení „vypnout“? Co by se stalo při zprávách mimo pořadí?
- Co by se změnilo, kdyby se Reviews ptal Orderingu přes HTTP „koupil to?“ – a co při výpadku Webu/Orderingu?
- Proč `SyncPurchasesHandler` neposílá anonymizované objednávky?
- Přidej do výlohy filtr „jen s hodnocením 4+“ – co musíš změnit v modulu Reviews? (Nic.)
