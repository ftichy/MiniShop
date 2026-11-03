---
title: Zrušení objednávky
summary: Potvrzenou nebo balenou objednávku jde zrušit – sága pak vrátí zboží na sklad (kompenzace). Doručení viz Expedice.
order: 4
---

> **Shrnutí:** Zákazník (nebo admin) zruší potvrzenou objednávku – od e-shop kroku 6 i během balení, po odeslání
> už ne. Zrušení spustí **kompenzaci** – příkaz `ReleaseStockV1`. Balení, odeslání a doručení (dřív „Vyřídit“)
> popisuje [Expedice](expedice.md).

## Zrušení

```mermaid
sequenceDiagram
    autonumber
    actor O as Obsluha
    participant S as OrderService
    participant ODB as ordering.db
    participant OW as OutboxWorker
    participant SG as OrderSagaOrchestrator
    participant H as Handlery (oznámení, překladač)
    participant MQ as RabbitMQ
    participant I as Inventory.Api

    O->>S: CancelAsync(id)
    S->>S: order.Cancel() – z Confirmed nebo Packing → OrderCancelled
    S->>ODB: COMMIT: Order Cancelled + Outbox[OrderCancelled]
    S-->>O: zrušeno
    OW->>SG: OrderCancelled
    SG->>SG: saga Reserved → ReleasingStock
    SG->>ODB: COMMIT: Outbox[ReleaseStockV1]
    OW->>H: OrderCancelled → oznámení + OrderCancelledV1
    OW->>MQ: ReleaseStockV1
    MQ->>I: ReleaseStockV1
    I->>I: vrátit zboží podle StockReservations
    I->>MQ: StockReleasedV1
    MQ->>SG: StockReleasedV1
    SG->>SG: saga → Released
```

## Co si všimnout

- `OrderService.CancelAsync` **o skladu neví** – jen změní stav. Vrácení zboží je reakce ságy na událost.
- Sklad vrací zboží podle **svých rezervací**, ne podle položek objednávky – pracuje jen s tím, co sám zná.
- Zrušit jde potvrzenou nebo balenou objednávku; `Pending` čeká na rozhodnutí ságy, `Shipped` je už na cestě
  (pravidlo hlídá entita `Order`). Souběh se současným odesláním řeší `Order.Version` – viz [Expedice](expedice.md).

## Kód

[OrderService](code:src/MiniShop.Application/Ordering/OrderService.cs) ·
[Order](code:src/MiniShop.Domain/Orders/Order.cs) ·
[ReleaseStockHandler](code:src/MiniShop.Application/Inventory/ReleaseStockHandler.cs)
