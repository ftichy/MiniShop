# Sekvenční diagram: vytvoření objednávky (stav po kroku 3)

![Vytvoření objednávky](vytvoreni-objednavky.png)

## Jak diagram číst

- **Fáze 1 (šedá):** `OrderService` připraví objednávku v paměti. Konstruktor `Order` vyvolá `OrderPlaced`,
  ale událost zatím jen leží v seznamu `DomainEvents` (krok 8). Nikdo ji ještě neslyší.
- **Fáze 2 (modrá):** `SaveChangesAsync` vybere události z entit (12–14) a uloží vše v jedné transakci (15–16).
  Objednávka teprve teď dostane `Id`.
- **Fáze 3 (zelená):** `MiniShopDbContext` předá události **dispatcheru** (17). Ten najde v DI všechny
  **handlery** `IDomainEventHandler<OrderPlaced>` (18) a postupně je zavolá (19 a 23).
  Pořadí handlerů odpovídá pořadí registrace: nejdřív aplikační (`AddApplication`), pak infrastrukturní.
- `OrderService` o dispatcheru ani handlerech nic neví – jen volá `SaveChangesAsync` (11).

Zrušení objednávky vypadá stejně, jen místo `OrderPlaced` putuje `OrderCancelled` a ve fázi 3
jsou tři handlery: `ReturnStockOnOrderCancelledHandler`, `SendCancellationNoticeHandler` a `OrderEventsLogHandler`.

## Zdroj diagramu (Mermaid)

Vykreslí se na GitHubu nebo ve VS Code s rozšířením *Markdown Preview Mermaid Support*.

```mermaid
sequenceDiagram
    autonumber
    actor K as Klient
    participant C as OrdersController
    participant S as OrderService
    participant O as Order (entita)
    participant DB as MiniShopDbContext
    participant SQL as SQLite
    participant D as DomainEventDispatcher
    participant H1 as SendOrderConfirmationHandler
    participant H2 as OrderEventsLogHandler
    participant N as LoggingOrderNotifier

    K->>C: POST /api/orders
    C->>S: PlaceOrderAsync(request)

    rect rgba(120, 120, 120, 0.08)
    Note over S,SQL: Fáze 1 – příprava objednávky (vše jen v paměti)
    S->>DB: products.GetByIdsAsync(ids)
    DB->>SQL: SELECT Products
    SQL-->>DB: produkty
    DB-->>S: produkty
    S->>O: new Order(jméno, e-mail, čas)
    O->>O: Raise(OrderPlaced)
    Note right of O: událost je jen v seznamu<br/>DomainEvents, nikdo ji neslyší
    loop každá položka
        S->>S: product.RemoveStock(qty)
        S->>O: AddItem(product, qty, sleva)
    end
    end

    rect rgba(37, 99, 235, 0.08)
    Note over S,SQL: Fáze 2 – uložení (jedna transakce)
    S->>DB: unitOfWork.SaveChangesAsync()
    DB->>O: DomainEvents
    O-->>DB: [OrderPlaced]
    DB->>O: ClearDomainEvents()
    DB->>SQL: INSERT Order + Items, UPDATE Products
    SQL-->>DB: OK (Order.Id = 7)
    end

    rect rgba(22, 163, 74, 0.08)
    Note over DB,N: Fáze 3 – rozeslání události (až po uložení)
    DB->>D: DispatchAsync([OrderPlaced])
    D->>D: najdi v DI všechny<br/>IDomainEventHandler<OrderPlaced>
    D->>H1: HandleAsync(OrderPlaced)
    H1->>N: OrderPlacedAsync(order)
    N-->>H1: zapsáno do logu
    H1-->>D: hotovo
    D->>H2: HandleAsync(OrderPlaced)
    H2->>H2: log „[Událost] OrderPlaced: objednávka 7“
    H2-->>D: hotovo
    D-->>DB: hotovo
    end

    DB-->>S: uloženo
    S-->>C: OrderDto
    C-->>K: 201 Created
```
