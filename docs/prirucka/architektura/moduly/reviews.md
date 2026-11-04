---
title: Modul Reviews
summary: Hodnocení produktů – hodnotí jen ten, kdo zboží koupil a dostal (vlastní kopie nákupů z událostí); souhrn jde do výlohy přes ProductRatingChangedV1.
order: 8
---

> **V jedné větě:** Reviews si z událostí objednávek drží **vlastní kopii nákupů** (kdo co dostal, co vrátil),
> podle ní pouští zákazníky k hodnocení a po každé změně oznámí **souhrn hodnocení** – výloha (Ordering) si ho uloží
> do kopie katalogu. Na Ordering se nikdy neptá. Běží ve službě **Web**, data má v `reviews.db`. Vznikl v e-shop kroku 14.

Tok a diagramy: [Hodnocení produktu](../../toky/hodnoceni-produktu.md).

## Součásti

| Součást | Co dělá | Kód |
| --- | --- | --- |
| **Review** | hodnocení 1–5 hvězd + text, jedno na zákazníka a produkt (upravit jde), skrytí adminem, autor „Jan N.“ | [Review.cs](code:src/MiniShop.Domain/Reviews/Review.cs) |
| **PurchasedOrder** | kopie objednávky: zákazník, produkty, doručeno, vráceno – `AllowsReviewOf` | [PurchasedOrder.cs](code:src/MiniShop.Domain/Reviews/PurchasedOrder.cs) |
| **PurchaseProjectionHandler** | plní kopii nákupů z událostí (upsert – zprávy mimo pořadí), zapomene zákazníka po smazání účtu | [PurchaseProjectionHandler.cs](code:src/MiniShop.Application/Reviews/PurchaseProjectionHandler.cs) |
| **ReviewService** | stránka produktu, odeslání/úprava, moderace; po každé změně souhrn do outboxu ve stejné transakci | [ReviewService.cs](code:src/MiniShop.Application/Reviews/ReviewService.cs) |
| **ReviewsDbContext** | `reviews.db`: `Reviews`, `PurchasedOrders` (+ `PurchasedProducts`), inbox, outbox | [ReviewsDbContext.cs](code:src/MiniShop.Infrastructure/Reviews/ReviewsDbContext.cs) |
| **PurchasesSyncOnStartupWorker** | prázdná kopie nákupů po startu → `SyncPurchasesV1` (nákupy z doby před modulem) | [PurchasesSyncOnStartupWorker.cs](code:src/MiniShop.Web/BackgroundServices/PurchasesSyncOnStartupWorker.cs) |
| **Stránky** | detail produktu `/Shop/{id}` (veřejný, formulář jen zákazníkovi), moderace `/Admin/Reviews` | [Shop/Detail](code:src/MiniShop.Web/Pages/Shop/Detail.cshtml.cs), [Admin/Reviews](code:src/MiniShop.Web/Pages/Admin/Reviews/Index.cshtml.cs) |

## Zprávy

| Směr | Zprávy |
| --- | --- |
| poslouchá (fronta `minishop.reviews`) | `OrderPlacedV2`, `OrderCompletedV1` (doručeno), `ReturnCompletedV1` (vráceno), `OrderSnapshotV2` (nákupy z doby před modulem), `CustomerAnonymizedV1` |
| posílá | `ProductRatingChangedV1` (souhrn pro výlohu – odebírá Ordering), `SyncPurchasesV1` (příkaz pro Ordering) |

## Pravidla

- **Hodnotit smí jen zákazník, který produkt dostal a nevrátil** – rozhoduje kopie nákupů, ne dotaz do Orderingu.
- **Jedno hodnocení na zákazníka a produkt** (unikátní index, mimo anonymizovaná). Druhé odeslání = úprava.
- **Vrácené zboží**: napsané hodnocení zůstane, nové ani úprava už ne.
- **Smazaný účet**: kopie nákupů se smaže, hodnocení zůstane jako „Smazaný zákazník“.
- **Skryté hodnocení** se nezobrazuje a nepočítá do průměru – skrytí pošle nový souhrn.
