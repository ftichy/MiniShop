---
title: Změna produktu
summary: Nový nebo změněný produkt ve skladu se během pár sekund promítne do kopie katalogu v Orderingu.
order: 5
---

> **Shrnutí:** Založení nebo změna produktu (název, popis, cena, kategorie) vyvolá `ProductChanged`, která se rovnou
> při uložení přeloží na `ProductUpdatedV1` a `ProductUpdatedV2` (e-shop krok 2: obě verze vedle sebe). Ordering si podle
> nich přepíše svou kopii – pokud změna není starší než to, co už zná.

## Nový produkt ze stránky Produkty

```mermaid
sequenceDiagram
    autonumber
    actor O as Obsluha
    participant UI as Web – stránka Produkty
    participant API as Inventory.Api
    participant PS as ProductService
    participant IDB as inventory.db
    participant MQ as RabbitMQ
    participant CH as CatalogProjectionHandler
    participant ODB as ordering.db

    O->>UI: přidat produkt
    UI->>API: POST /api/products (InventoryApiClient)
    API->>PS: CreateAsync
    PS->>PS: new Product(...) → ProductChanged
    PS->>IDB: COMMIT: Products + Outbox[ProductUpdatedV1, ProductUpdatedV2]
    API-->>UI: 201 Created
    Note over IDB,MQ: kolo outbox workeru skladu (≤ 2 s)
    IDB->>MQ: ProductUpdatedV1, ProductUpdatedV2
    MQ->>CH: fronta minishop.ordering (přes inbox, každá verze zvlášť)
    CH->>ODB: nový CatalogProduct (V2: + popis, kategorie, dostupnost)
    Note over ODB: produkt je ve výloze i v nabídce formuláře objednávky
```

## Změna produktu nebo kategorie

E-shop krok 2: admin produkt upraví na stránce [Úprava produktu](code:src/MiniShop.Web/Pages/Products/Edit.cshtml.cs)
(nebo přes REST API skladu `PUT /api/products/{id}`). Tok je stejný – jen místo `new Product` proběhne
`product.Update(...)` a projekce zavolá `CatalogProduct.Apply` / `ApplyDetails`, které **starší změnu ignorují**
(zprávy můžou přijít v jiném pořadí).

**Přejmenování kategorie** ([CategoryService](code:src/MiniShop.Application/Inventory/CategoryService.cs)) zavolá
`product.CategoryRenamed` u každého produktu kategorie – jméno kategorie je součástí zprávy o produktu, takže se ho
výloha dozví od produktů. Kategorie sama žádnou zprávu nemá.

## Co si všimnout

- **Stránka Produkty je ve Webu, ale data patří skladu** – UI volá Inventory.Api přes HTTP. Business důsledek
  (kopie katalogu) ale jde přes zprávu.
- **Naskladnění** pošle zprávu, jen když produkt přejde do jiného pásma dostupnosti – viz
  [Změna dostupnosti](zmena-dostupnosti.md).
- **Smazání** pošle `ProductRemovedV1` a kopie produkt jen označí jako odebraný.
- Objednávky, které už existují, si nesou **snímek** ceny z doby objednání – změna je neovlivní.

## Kód

[ProductService](code:src/MiniShop.Application/Inventory/ProductService.cs) ·
[Product](code:src/MiniShop.Domain/Inventory/Product.cs) ·
[InventoryDbContext](code:src/MiniShop.Infrastructure/Inventory/InventoryDbContext.cs) ·
[CatalogProjectionHandler](code:src/MiniShop.Application/Ordering/Catalog/CatalogProjectionHandler.cs) ·
[CatalogProduct](code:src/MiniShop.Domain/Orders/CatalogProduct.cs)
