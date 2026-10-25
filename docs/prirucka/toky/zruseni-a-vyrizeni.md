---
title: Zrušení a vyřízení objednávky
summary: Potvrzenou objednávku jde vyřídit (konec ságy) nebo zrušit – pak sága vrátí zboží na sklad.
order: 4
---

> **Shrnutí:** Obsluha u potvrzené objednávky klikne na *Vyřídit* nebo *Zrušit*. Vyřízení ságu jen uzavře
> (zboží zůstává prodané). Zrušení spustí **kompenzaci** – příkaz `ReleaseStockV1`.

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
    S->>S: order.Cancel() – jen z Confirmed → OrderCancelled
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

## Vyřízení

```mermaid
sequenceDiagram
    autonumber
    actor O as Obsluha
    participant S as OrderService
    participant OW as OutboxWorker
    participant SG as OrderSagaOrchestrator
    participant H as Handlery

    O->>S: CompleteAsync(id)
    S->>S: order.Complete() – jen z Confirmed → OrderCompleted
    S-->>O: vyřízeno
    OW->>SG: OrderCompleted
    SG->>SG: saga Reserved → Completed (žádná akce)
    OW->>H: oznámení + OrderCompletedV1
```

## Co si všimnout

- `OrderService.CancelAsync` **o skladu neví** – jen změní stav. Vrácení zboží je reakce ságy na událost.
- Sklad vrací zboží podle **svých rezervací**, ne podle položek objednávky – pracuje jen s tím, co sám zná.
- Zrušit jde jen potvrzenou objednávku; `Pending` čeká na rozhodnutí ságy (pravidlo hlídá entita `Order`).

## Kód

[OrderService](code:src/MiniShop.Application/Ordering/OrderService.cs) ·
[Order](code:src/MiniShop.Domain/Orders/Order.cs) ·
[ReleaseStockHandler](code:src/MiniShop.Application/Inventory/ReleaseStockHandler.cs)
