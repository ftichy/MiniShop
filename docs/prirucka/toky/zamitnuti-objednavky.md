---
title: Zamítnutí objednávky
summary: Sklad nemá dost zboží – nezarezervuje nic a sága objednávku zamítne s důvodem.
order: 2
---

> **Shrnutí:** Nedostatek zboží není chyba požadavku (HTTP 400), ale **business výsledek**. Sklad zkontroluje
> všechny položky, a když některá nestačí, nezarezervuje **nic** a odpoví `StockReservationFailedV1` s důvodem.

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant W as Web (Ordering)
    participant SG as OrderSagaOrchestrator
    participant MQ as RabbitMQ
    participant RH as ReserveStockHandler
    participant IDB as inventory.db

    Z->>W: objednat 50 ks (skladem 3)
    W-->>Z: přijato (Pending)
    Note over W,MQ: sága pošle ReserveStockV1 (jako při vytvoření)
    MQ->>RH: ReserveStockV1
    RH->>IDB: zkontrolovat VŠECHNY položky
    RH->>RH: chybí: „Klávesnice: požadováno 50 ks, skladem 3 ks“
    RH->>IDB: COMMIT: žádná změna zásob, Outbox[StockReservationFailedV1]
    RH->>MQ: StockReservationFailedV1 (důvod)
    MQ->>SG: StockReservationFailedV1
    SG->>SG: saga → Rejected, akce RejectOrder
    SG->>W: Order.Reject(důvod) → OrderRejected
    W->>W: SendRejectionNoticeHandler: omluva zákazníkovi (log)
```

## Co si všimnout

- **Všechno, nebo nic.** Sklad nejdřív zkontroluje všechny položky a teprve pak odečítá. Nikdy nezarezervuje půlku
  objednávky – nebylo by co kompenzovat.
- **Důvod putuje až k zákazníkovi** – v UI je u zamítnuté objednávky vidět, které zboží chybělo.
- Kontrola zásoby ve formuláři (údaj ze skladu přes HTTP) je jen **orientační**. Rozhoduje až rezervace – mezi
  zobrazením formuláře a objednáním mohl zboží koupit někdo jiný.

## Kód

[ReserveStockHandler](code:src/MiniShop.Application/Inventory/ReserveStockHandler.cs) ·
[OrderSaga.StockReservationFailed](code:src/MiniShop.Domain/Orders/Saga/OrderSaga.cs) ·
[SendRejectionNoticeHandler](code:src/MiniShop.Application/Ordering/EventHandlers/SendRejectionNoticeHandler.cs)
