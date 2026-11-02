---
title: Pokladna
summary: Košík → objednávka v jedné transakci – verze košíku, změna ceny, dvojí odeslání a souběh.
order: 10
---

> **Shrnutí:** Pokladna je jediné místo, kde se nezávazný košík mění v závazek. Formulář nese **verzi košíku**
> (`CheckoutId`). Podle ní server pozná, že zákazník objednává přesně to, co viděl, a že stejné odeslání už
> objednávku založilo. Objednávka a vyprázdněný košík se uloží **v jedné transakci**. Dál jde všechno stejně jako
> v [Vytvoření objednávky](vytvoreni-objednavky.md): sága, sklad, potvrzení. Přirovnání: u pokladny v obchodě
> se zboží namarkuje, zaplatí a vozík se vyprázdní – nikdy jen půlka z toho.

## Běžné objednání

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant P as Checkout/Index
    participant CS as CheckoutService
    participant C as Cart (agregát)
    participant ODB as ordering.db

    Z->>P: GET /Checkout
    P->>CS: GetAsync(zákazník)
    CS->>ODB: košík + produkty z kopie katalogu + poslední adresa
    CS-->>P: řádky, slevy, doprava, CheckoutId = verze košíku
    P-->>Z: formulář (skryté pole CheckoutId)

    Z->>P: POST Objednat (CheckoutId, doprava, adresa)
    P->>CS: PlaceOrderAsync
    CS->>ODB: objednávka s tímto CheckoutId už je? (ne)
    CS->>C: CheckOut(CheckoutId, katalog)
    C->>C: stejná verze? nic staženého ani vyprodaného? ceny beze změny?
    C-->>CS: řádky + košík prázdný, nová verze
    CS->>CS: new Order(…, CheckoutId), AddItem (+ sleva), SetShipping (cena ze strategie)
    CS->>ODB: COMMIT: Orders + Carts (UPDATE … WHERE CheckoutId = původní) + Outbox[OrderPlaced]
    P-->>Z: 302 → /Orders „objednávka přijata, ověřujeme sklad…“
    Note over ODB: dál sága → ReserveStockV1 se všemi položkami → sklad zarezervuje všechno, nebo nic
```

## Dvojí odeslání a souběh

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant A as Požadavek 1
    participant B as Požadavek 2
    participant ODB as ordering.db

    Z->>A: klik „Objednat“
    Z->>B: klik znovu (dvojklik)
    A->>ODB: objednávka s CheckoutId X? ne
    B->>ODB: objednávka s CheckoutId X? ne
    A->>ODB: COMMIT objednávka + košík (verze X → Y)
    B->>ODB: COMMIT: UPDATE Carts … WHERE CheckoutId = X → 0 řádků
    ODB-->>B: konflikt → celá transakce zrušena (ConcurrencyConflictException)
    B->>ODB: objednávka s CheckoutId X? ano → vrátit ji
    Note over Z: obě odpovědi ukazují stejnou objednávku
```

- **Postupně (F5 po odeslání):** druhý požadavek najde objednávku podle `CheckoutId` hned na začátku – nic nezakládá.
- **Současně:** verze košíku je *concurrency token* (EF hlídá `WHERE CheckoutId = původní`) a `Orders.CheckoutId`
  má unikátní index. Kterákoli z těch dvou pojistek zruší druhou transakci. Infrastruktura ji převede na
  `ConcurrencyConflictException` (aplikační vrstva o EF Core neví) a pokladna vrátí objednávku prvního požadavku.
- **REST API** to umí taky: `POST /api/orders` s volitelným `checkoutId` (klient si ho vygeneruje a při opakování
  pošle stejné).

## Změna ceny a starý formulář

| Situace | Co se stane |
| --- | --- |
| Admin změní cenu produktu v košíku | Pokladna ukáže „Ceny se změnily“ a tlačítko **Přijmout nové ceny**; dokud je zákazník nepotvrdí, `Cart.CheckOut` objednávku nepustí. Žádné tiché zdražení ani zlevnění. |
| Zákazník mezitím změní košík v jiné záložce | Každá změna košíku = nová verze. Starý formulář pokladny neprojde („Košík se mezitím změnil“), stránka se ukáže znovu s aktuálním obsahem. |
| Produkt se vyprodá nebo zmizí z nabídky | Pokladna ho označí a objednat nejde, dokud ho zákazník neodebere. |
| Sklad mezitím nemá dost kusů | Pokladna to nepozná (zásobu zná jen sklad). Objednávka vznikne a sága ji zamítne s důvodem, který produkt chybí – viz [Zamítnutí objednávky](zamitnuti-objednavky.md). |

Kód: [CheckoutService.cs](code:src/MiniShop.Application/Ordering/Checkout/CheckoutService.cs),
`Cart.CheckOut` v [Cart.cs](code:src/MiniShop.Domain/Orders/Carts/Cart.cs),
[Checkout/Index.cshtml.cs](code:src/MiniShop.Web/Pages/Checkout/Index.cshtml.cs),
převod výjimek v [ModuleDbContext.cs](code:src/MiniShop.Infrastructure/Persistence/ModuleDbContext.cs).
