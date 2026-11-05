---
title: Hodnocení produktu
summary: Kopie nákupů z událostí rozhodne, kdo smí hodnotit; souhrn hodnocení putuje do kopie katalogu a výloha ukáže hvězdičky bez dotazu do modulu Reviews.
order: 16
---

> **Shrnutí:** Hodnotit smí jen ten, kdo produkt **koupil a dostal**. Modul Reviews se kvůli tomu neptá Orderingu –
> drží si **vlastní kopii nákupů** z událostí, které už existují (`OrderPlacedV2`, `OrderCompletedV1`,
> `ReturnCompletedV1`). Po každém hodnocení oznámí souhrn `ProductRatingChangedV1` a Ordering si ho uloží do kopie
> katalogu – výloha pak ukáže hvězdičky i řazení „nejlépe hodnocené“ bez jediného dotazu navíc.

Přirovnání: recenzi píše jen ten, kdo má účtenku – a prodejna si kopie účtenek drží sama, nechodí se ptát do účtárny.

## Odeslání hodnocení

```mermaid
sequenceDiagram
    autonumber
    participant O as Ordering
    participant MQ as RabbitMQ
    participant R as Reviews (Web)
    participant RDB as schéma reviews
    actor Z as Zákazník
    participant C as Kopie katalogu (Ordering)

    O->>MQ: OrderPlacedV2 (kdo, produkty) … později OrderCompletedV1 (doručeno)
    MQ->>R: PurchaseProjectionHandler → PurchasedOrder (doručeno)
    Z->>R: /Shop/{id} – Odeslat hodnocení (4 hvězdy)
    R->>RDB: smí? FindReviewableAsync (doručeno, nevráceno, produkt v objednávce)
    R->>RDB: COMMIT: Review + Outbox[ProductRatingChangedV1 – průměr, počet]
    RDB-->>MQ: ProductRatingChangedV1
    MQ->>C: CatalogProjectionHandler → CatalogProduct.ApplyRating (starší souhrn novější nepřepíše)
    Note over C: výloha /Shop – hvězdičky a řazení „nejlépe hodnocené“ z kopie katalogu
```

## Nákupy z doby před modulem

Modul Reviews vznikl, když už objednávky existovaly. Po startu s prázdnou kopií nákupů si o ně řekne – stejný vzor
jako obnova katalogu nebo přestavění přehledu tržeb:

```mermaid
sequenceDiagram
    autonumber
    participant W as PurchasesSyncOnStartupWorker
    participant R as Reviews
    participant O as Ordering (SyncPurchasesHandler)

    W->>R: SyncPurchasesIfEmptyAsync – kopie nákupů je prázdná
    R->>O: SyncPurchasesV1 (příkaz přes outbox a RabbitMQ)
    O->>R: OrderSnapshotV2 pro každou objednávku (zákazník, položky, stav) – anonymizované ne
    R->>R: PurchaseProjectionHandler – stejný upsert jako u událostí
```

**Proč `OrderSnapshotV2`, a ne `OrderSnapshotV1`?** V1 (přehled tržeb) záměrně nenese zákazníka – přehled osobní
údaje nepotřebuje. Reviews ano (kdo co koupil). Nová verze kontraktu místo změny staré: odběratelé V1 se nemění.

## Na co si dát pozor

- **Zprávy mimo pořadí:** doručení může dorazit dřív než vznik. `PurchasedOrder` se založí hned a vznik ho doplní;
  doručení i vrácení se jen zapínají – výsledek nezávisí na pořadí.
- **Souhrn ve stejné transakci** jako hodnocení (outbox) – průměr ve výloze nikdy „neuteče“ od hodnocení.
- **Osobní údaje:** autor jen „Jan N.“; po smazání účtu „Smazaný zákazník“ a kopie nákupů pryč (`CustomerAnonymizedV1`).
- Stránka produktu spojuje dva moduly (katalog z Orderingu, hodnocení z Reviews) – spojuje je až Web, moduly se neznají.

## Kód

[ReviewService](code:src/MiniShop.Application/Reviews/ReviewService.cs) ·
[PurchaseProjectionHandler](code:src/MiniShop.Application/Reviews/PurchaseProjectionHandler.cs) ·
[SyncPurchasesHandler](code:src/MiniShop.Application/Ordering/IntegrationEvents/SyncPurchasesHandler.cs) ·
[CatalogProjectionHandler](code:src/MiniShop.Application/Ordering/Catalog/CatalogProjectionHandler.cs) ·
[ProductRatingChangedV1](code:src/MiniShop.Contracts/Reviews/ProductRatingChangedV1.cs) ·
[SyncPurchasesV1](code:src/MiniShop.Contracts/Reviews/SyncPurchasesV1.cs) ·
[OrderSnapshotV2](code:src/MiniShop.Contracts/Orders/OrderSnapshotV2.cs)
