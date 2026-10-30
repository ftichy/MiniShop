---
title: Obnova katalogu
summary: Když je kopie katalogu prázdná (nová databáze) nebo neúplná, Ordering požádá sklad, aby znovu poslal všechny produkty.
order: 6
---

> **Shrnutí:** Události o produktech chodí jen při změně. Nová `ordering.db` by tak zůstala bez katalogu.
> Proto Web po startu (nebo tlačítkem) pošle příkaz `SyncCatalogV1` a sklad odpoví `ProductUpdatedV1` a `ProductUpdatedV2`
> pro každý produkt. E-shop krok 2: obnova proběhne i u **neúplné** kopie – produkty z doby před výlohou nemají
> dostupnost a kategorii (migrace jim dala `Unknown`), po nasazení je tak Web doplní sám.

```mermaid
sequenceDiagram
    autonumber
    participant SW as CatalogSyncOnStartupWorker
    participant CS as CatalogSyncService
    participant ODB as ordering.db
    participant MQ as RabbitMQ
    participant SH as SyncCatalogHandler (sklad)
    participant CH as CatalogProjectionHandler

    SW->>CS: RequestIfEmptyOrIncompleteAsync() – po startu Webu
    CS->>ODB: je kopie prázdná, nebo má produkt bez dostupnosti?
    alt prázdná nebo neúplná
        CS->>ODB: COMMIT: Outbox[SyncCatalogV1]
        ODB->>MQ: SyncCatalogV1 (kolo workeru)
        MQ->>SH: fronta minishop.inventory
        loop pro každý produkt
            SH->>MQ: ProductUpdatedV1 + V2 (nová EventId, čas = Product.ChangedAtUtc)
        end
        MQ->>CH: ProductUpdatedV1, ProductUpdatedV2 …
        CH->>ODB: CatalogProduct přidat / Apply / ApplyDetails + dostupnost
    else už něco obsahuje
        CS-->>SW: nic nedělat
    end
```

## Co si všimnout

- **Čas zprávy = čas poslední změny produktu**, ne „teď“. Kdyby byl „teď“, mohl by znovu poslaný starý stav
  přebít novější změnu, která je zrovna na cestě.
- **Nové `EventId`** – jinak by inbox Orderingu zprávu považoval za už zpracovanou.
- Žádost jde **přes outbox** – když sklad nebo broker zrovna neběží, počká a odejde později.
- Ručně: tlačítko na stránce *Objednávky*.

## Známé omezení

Obnova produkty nemaže – produkt, jehož `ProductRemovedV1` se ztratil, v kopii zůstane.

## Kód

[CatalogSyncOnStartupWorker](code:src/MiniShop.Web/BackgroundServices/CatalogSyncOnStartupWorker.cs) ·
[CatalogSyncService](code:src/MiniShop.Application/Ordering/Catalog/CatalogSyncService.cs) ·
[SyncCatalogHandler](code:src/MiniShop.Application/Inventory/SyncCatalogHandler.cs)
