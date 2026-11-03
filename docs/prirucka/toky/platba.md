---
title: Platba kartou
summary: Rezervace → čekání na platbu → podepsaný webhook → potvrzení; zamítnutí, timeout a vrácení pozdní platby.
order: 12
---

> **Shrnutí:** U platby kartou sága po rezervaci zboží objednávku nepotvrdí hned. Pošle příkaz `RequestPaymentV1`
> modulu Payments a čeká – nejvýš `Sagas:PaymentTimeoutMinutes` (30 min). Zákazník zaplatí na stránce brány, brána
> pošle **podepsaný webhook**, Payments oznámí `PaymentSucceededV1` a sága objednávku potvrdí. Dobírka platbu přeskočí.

## Zaplaceno

```mermaid
sequenceDiagram
    autonumber
    actor Z as Zákazník
    participant SG as OrderSagaOrchestrator
    participant MQ as RabbitMQ
    participant P as Payments
    participant B as Brána (/Pay)
    participant H as /api/payments/webhook

    Note over SG: sklad zarezervoval (StockReservedV1)
    SG->>SG: saga AwaitingReservation → AwaitingPayment (termín 30 min)
    SG->>MQ: Order AwaitingPayment + RequestPaymentV1 (částka, termín)
    MQ->>P: RequestPaymentV1 → Payment Pending, stránka brány
    Z->>B: Zaplatit
    B->>H: webhook {paymentId, succeeded} + podpis HMAC
    H->>H: podpis sedí? (jinak 401, nic se nezmění)
    H->>P: Payment Succeeded → Outbox[PaymentSucceededV1]
    P->>MQ: PaymentSucceededV1
    MQ->>SG: saga → Reserved, Order Confirmed → e-mail „potvrzená“
```

## Nezaplaceno – zamítnutí nebo vypršení

```mermaid
sequenceDiagram
    autonumber
    participant SG as OrderSagaOrchestrator
    participant TW as OrderSagaTimeoutWorker
    participant MQ as RabbitMQ
    participant I as Inventory
    participant P as Payments

    alt zákazník na bráně klikl Zamítnout
        P->>MQ: PaymentFailedV1 (důvod)
        MQ->>SG: saga AwaitingPayment → ReleasingStock
    else 30 minut bez platby
        TW->>SG: TimeOutAsync – termín platby prošel
    end
    SG->>MQ: Order Rejected (důvod) + ReleaseStockV1 – v jedné transakci
    MQ->>I: vrátit zboží → StockReleasedV1 → saga Released
```

## Pozdní platba = vrácení peněz

Zákazník zaplatí ve 30. minutě a 1. vteřině – sága už objednávku zamítla a zboží vrátila. Brána ale peníze
vzala a `PaymentSucceededV1` dorazí. Sága ve stavu `Rejected` / `ReleasingStock` / `Released` pošle
**`RefundPaymentV1`** (kompenzace, jen jednou – `RefundRequested`), Payments vrátí peníze a oznámí
`PaymentRefundedV1`, zákazník dostane e-mail „platbu vracíme“. Stejný vzor jako pozdní rezervace po timeoutu
skladu ([Timeout a kompenzace](timeout-a-kompenzace.md)). Simulovaná brána po termínu zaplatit nepustí, skutečná
by mohla.

## Co si všimnout

- **Webhook bez přihlášení, ale s podpisem.** Brána účet nemá; kdo nezná tajemství, podpis nespočítá
  (401). Opakovaný webhook vrátí 200 a nic nezdvojí – brány posílají znovu, dokud nedostanou 2xx.
- **Zamítnutí, ne zrušení.** Nezaplacená objednávka je `Rejected` („systém ji nemohl vyřídit“) – `Cancelled`
  znamená „zrušil člověk“. Zrušit jde i objednávku, která na platbu čeká (e-shop krok 9) – stránka brány pak
  platit nedovolí a zaplacenou objednávku sága při zrušení vrátí (`RefundPaymentV1`), viz [Zrušení](zruseni-a-vyrizeni.md).
- **Zboží drží rezervace** po celou dobu čekání na platbu – proto termín.
- Zákazník po pokladně skončí na detailu objednávky s tlačítkem **Zaplatit**; detail se obnovuje, dokud se
  na platbu čeká.

## Kód

[OrderSaga](code:src/MiniShop.Domain/Orders/Saga/OrderSaga.cs) ·
[OrderSagaOrchestrator](code:src/MiniShop.Application/Ordering/Saga/OrderSagaOrchestrator.cs) ·
[Payment](code:src/MiniShop.Domain/Payments/Payment.cs) ·
[PaymentWebhookProcessor](code:src/MiniShop.Application/Payments/PaymentWebhookProcessor.cs) ·
[Pay/Index](code:src/MiniShop.Web/Pages/Pay/Index.cshtml.cs)
