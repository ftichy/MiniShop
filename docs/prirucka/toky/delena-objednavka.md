---
title: Dělená objednávka (víc skladů)
summary: Objednávka ze dvou skladů – sklad ji rozdělí na balíky, každý balík jede vlastní zásilkou, doručeno je až po všech; vrácený balík = automatická vratka po částech.
order: 20
---

> **Shrnutí:** Odkud se zboží vezme, rozhoduje **sklad** (přidělení zásob – jeden sklad, když to jde). Když to nejde, pošle
> Orderingu rozdělení jako **balíky** (`StockReservedV2`). Každý balík předá jeho sklad dopravci zvlášť (`CreateShipmentV2`),
> objednávka je odeslaná s posledním balíkem a doručená, až skončí všechny (scatter-gather v agregátu `Order`).
> Balík, který dopravce vrátí, se kompenzuje sám – **automatickou vratkou po částech**. Plán směru:
> [Sklady-roadmap](code:docs/Sklady-roadmap.md); moduly [Inventory](../architektura/moduly/inventory.md),
> [Ordering](../architektura/moduly/ordering.md), [Shipping](../architektura/moduly/shipping.md).

## Rozdělení a odeslání po balících

```mermaid
sequenceDiagram
    autonumber
    participant O as Ordering (Web)
    participant I as Inventory.Api
    participant SH as Shipping (Web)
    participant C as Dopravce
    participant N as Notifications.Api

    O->>I: ReserveStockV1 (myš 1×, kabel 2×)
    I->>I: StockAllocator – žádný sklad nemá všechno → Praha: myš, Brno: kabel
    I-->>O: StockReservedV1 (sága potvrdí) + StockReservedV2 { balíky PRG, BRQ }
    O->>O: Order.SetParcels → balík 1 = Brno, balík 2 = Praha
    Note over O: Expedice – každý sklad předá svůj balík
    O->>SH: CreateShipmentV2 { balík 1, Brno, dobírka balíku }
    SH->>C: POST /v1/shipments (Idempotency-Key = CommandId)
    SH-->>O: ShipmentCreatedV1 + ShipmentCreatedV2 { balík 1, CS… }
    O->>O: ConfirmParcelShipment(1) – objednávka dál „K odeslání“
    O-->>N: ParcelShippedV1 → e-mail + push „Balík 1 z 2 je na cestě“
    O-->>I: ParcelShippedV1 → sklad Brno vydá své rezervace
    O->>SH: CreateShipmentV2 { balík 2, Praha }
    SH-->>O: ShipmentCreatedV2 { balík 2 }
    O->>O: poslední balík → OrderShipped
    O-->>I: OrderShippedV1 (vydat zbytek)
    O-->>N: OrderShippedV1 → e-mail „na cestě“
    C-->>SH: webhooky (číslo zásilky)
    SH-->>O: ShipmentDeliveredV1 (CS… balíku 1), pak balíku 2
    O->>O: MarkParcelDelivered – po posledním → OrderCompleted (doručeno)
```

## Vrácený balík = vratka po částech

```mermaid
sequenceDiagram
    autonumber
    participant SH as Shipping (Web)
    participant O as Ordering (Web)
    participant RS as ReturnSagaOrchestrator
    participant I as Inventory.Api
    participant P as Payments
    participant F as Invoicing

    SH-->>O: ShipmentReturnedV1 (číslo zásilky balíku 1)
    O->>O: ReturnParcelToSender → OrderReturn s položkami balíku → OrderParcelReturnedToSender
    O->>RS: (doménová událost) sága začne rovnou naskladněním – zboží už je zpátky
    RS->>I: RestockReturnedGoodsV2 (jen kusy balíku – do skladu původu)
    I-->>RS: ReturnedGoodsRestockedV2
    alt platba kartou
        RS->>P: RefundPaymentV2 (částka balíku, bez dopravy)
        P-->>RS: PaymentRefundedV2
    end
    RS->>O: CompletePartialReturn → ReturnCompletedV2
    O-->>F: ReturnCompletedV2 – s fakturou dobropis za balík, dobírka bez faktury = odečíst z kopie
    Note over O: druhý balík doručen = doručeno, vrátily se všechny = vráceno dopravcem (bez OrderReturnedToSenderV1)
```

## Na co si dát pozor

- **Sága objednávky se nemění** – na balíky nečeká, potvrzení dál řídí `StockReservedV1`.
- **Doručení a vrácení bez nových verzí** – zprávy nesou číslo zásilky a podle něj Ordering najde balík.
- **Kompenzace jen jednou:** každý vrácený balík má svou vratku; kdyby se po vrácení všech balíků poslalo ještě
  `OrderReturnedToSenderV1`, peníze i zboží by se vracely podruhé.
