---
title: Vytvoření objednávky
summary: Hlavní tok – od odeslání formuláře přes ságu a rezervaci skladu až po potvrzení zákazníkovi.
order: 1
---

> **Shrnutí:** Web objednávku uloží jako `Pending` a hned odpoví. Na pozadí sága pošle skladu příkaz
> `ReserveStockV1`, sklad zboží zarezervuje a odpoví `StockReservedV1`, sága objednávku potvrdí a zákazník
> dostane potvrzení. Celé to trvá **~4–8 s**.

## Zjednodušeně

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant W as Web (Ordering)
    participant MQ as RabbitMQ
    participant I as Inventory.Api

    Z->>W: odeslat formulář
    W-->>Z: objednávka přijata (Pending)
    W->>MQ: ReserveStockV1
    MQ->>I: ReserveStockV1
    I->>I: zarezervovat zboží
    I->>MQ: StockReservedV1
    MQ->>W: StockReservedV1
    W->>W: objednávka Confirmed + potvrzení zákazníkovi
```

## Podrobně

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    box rgb(232,242,255) Web – modul Ordering
    participant P as Orders/Index
    participant S as OrderService
    participant ODB as ordering.db
    participant OW as OutboxWorker
    participant SG as OrderSagaOrchestrator
    participant TR as Překladač + log
    participant OC as Consumer ordering
    end
    participant MQ as RabbitMQ
    box rgb(234,248,238) Inventory.Api
    participant IC as Consumer inventory
    participant RH as ReserveStockHandler
    participant IDB as inventory.db
    participant IW as OutboxWorker
    end

    rect rgb(245,245,245)
    Note over Z,ODB: Fáze 1 – HTTP požadavek (synchronně)
    Z->>P: POST formulář (produkt, množství) + cookie
    P->>S: PlaceOrderAsync(request, zákazník z cookie: ID, jméno, e-mail)
    S->>ODB: načíst produkty z kopie katalogu
    S->>S: new Order(CustomerId, …) → OrderPlaced, AddItem (+ sleva)
    S->>ODB: COMMIT: Orders + Outbox[OrderPlaced]
    P->>P: Journeys: OrderSubmitted
    P-->>Z: přesměrování, objednávka Pending
    end

    rect rgb(232,242,255)
    Note over OW,TR: Fáze 2 – kolo outbox workeru (≤ 2 s)
    OW->>ODB: vybrat OrderPlaced
    OW->>SG: OrderPlaced (přes inbox)
    SG->>ODB: COMMIT: OrderSagas (AwaitingReservation) + Outbox[ReserveStockV1]
    OW->>TR: OrderPlaced (přes inbox)
    TR->>ODB: COMMIT: Outbox[OrderPlacedV1]
    end

    rect rgb(232,242,255)
    Note over OW,MQ: Fáze 3 – další kolo: integrační zprávy ven
    OW->>MQ: publish ReserveStockV1 (čeká na potvrzení brokeru)
    OW->>MQ: publish OrderPlacedV1 (čte jen audit)
    end

    rect rgb(234,248,238)
    Note over MQ,IW: Fáze 4 – sklad
    MQ->>IC: ReserveStockV1 (fronta minishop.inventory)
    IC->>RH: přes inbox
    RH->>IDB: zkontrolovat všechny položky
    RH->>IDB: COMMIT: zásoby − N, StockReservations, Outbox[StockReservedV1], inbox
    IW->>MQ: publish StockReservedV1 (≤ 2 s)
    end

    rect rgb(232,242,255)
    Note over MQ,OC: Fáze 5 – potvrzení
    MQ->>OC: StockReservedV1 (fronta minishop.ordering)
    OC->>SG: přes inbox
    SG->>SG: saga.StockReserved → ConfirmOrder
    SG->>ODB: COMMIT: sága Reserved, Order Confirmed, Outbox[OrderConfirmed]
    OW->>TR: OrderConfirmed → SendOrderConfirmationHandler (≤ 2 s)
    TR->>TR: „Objednávka potvrzena…“ (log místo e-mailu)
    end
```

## Co si všimnout

- **Kroky 1–7 jsou jediné synchronní.** Zákazník nečeká na sklad – dostane `Pending` a stránka se sama obnoví
  (jen když zrovna nic nevyplňuje).
- **Každý `COMMIT` je jedna lokální transakce** v jedné databázi. Mezi databázemi putují jen zprávy.
- **Mezi fázemi jsou časové mezery** (kolo workeru 2 s, doručení přes RabbitMQ). Proto potvrzení trvá několik sekund.
- **Každé převzetí zprávy jde přes inbox** – kdyby se cokoli doručilo dvakrát, nic se nezdvojí.
- Zprávu `OrderPlacedV1` zatím nikdo nezpracovává (jen auditní log). Je připravená pro budoucí odběratele
  (např. věrnostní program) – ti se přidají **bez změny Orderingu**.

## Kód

[Orders/Index.cshtml.cs](code:src/MiniShop.Web/Pages/Orders/Index.cshtml.cs) ·
[OrderService](code:src/MiniShop.Application/Ordering/OrderService.cs) ·
[OrderSagaOrchestrator](code:src/MiniShop.Application/Ordering/Saga/OrderSagaOrchestrator.cs) ·
[ReserveStockHandler](code:src/MiniShop.Application/Inventory/ReserveStockHandler.cs) ·
[test celé cesty](code:tests/MiniShop.Tests/Application/OrderServiceTests.cs)

## Související

- Co když zboží nestačí: [Zamítnutí objednávky](zamitnuti-objednavky.md)
- Co když sklad mlčí: [Timeout a kompenzace](timeout-a-kompenzace.md)
- Detail jedné zprávy: [Cesta jedné zprávy](cesta-zpravy.md)
