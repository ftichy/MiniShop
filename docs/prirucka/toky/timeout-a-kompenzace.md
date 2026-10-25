---
title: Timeout a kompenzace
summary: Sklad neodpověděl do 30 s – sága objednávku zamítne; když rezervace dorazí pozdě, zboží se vrátí.
order: 3
---

> **Shrnutí:** Objednávka nesmí viset věčně. Když sklad neodpoví do `Sagas:ReservationTimeoutSeconds` (30 s),
> hlídač ji zamítne. Pokud sklad mezitím zboží přece jen zarezervoval, sága pošle `ReleaseStockV1` – **kompenzace**.

## Scénář: sklad byl chvíli mimo provoz

```mermaid
sequenceDiagram
    autonumber
    participant SG as OrderSagaOrchestrator
    participant TW as OrderSagaTimeoutWorker
    participant ODB as ordering.db
    participant MQ as RabbitMQ
    participant I as Inventory.Api (nejede)

    SG->>MQ: ReserveStockV1
    Note over MQ,I: zpráva čeká ve frontě minishop.inventory
    loop každých 5 s
        TW->>ODB: ságy AwaitingReservation po termínu?
    end
    TW->>SG: TimeOutAsync(orderId) – vlastní trasa
    SG->>SG: saga → Rejected („Sklad neodpověděl včas.“)
    SG->>ODB: COMMIT: Order Rejected, OrderRejected
    Note over I: sklad naběhne a zpracuje frontu
    MQ->>I: ReserveStockV1
    I->>I: zarezervovat (zboží je)
    I->>MQ: StockReservedV1 (pozdě!)
    MQ->>SG: StockReservedV1
    SG->>SG: saga Rejected + StockReserved → ReleasingStock
    SG->>MQ: ReleaseStockV1 (kompenzace)
    MQ->>I: ReleaseStockV1
    I->>I: vrátit zboží podle rezervací
    I->>MQ: StockReleasedV1
    MQ->>SG: StockReleasedV1
    SG->>SG: saga → Released (konec)
```

## Co si všimnout

- **Zprávy se neztratí** – i když sklad neběží, příkaz čeká ve frontě RabbitMQ.
- **Objednávka se nevrací do hry.** Zamítnutí platí; pozdní rezervace se jen napraví vrácením zboží.
- **Souběh timeoutu a odpovědi**: když přijdou současně, sága má *concurrency token* (`Version`) – jeden zápis
  vyhraje, druhý se zopakuje nad novým stavem a zjistí, že už nemá co dělat.
- Timeout začíná **novou trasu** – v dashboardu není pod původním požadavkem (nevyvolal ho uživatel).

## Vyzkoušejte

1. Spusťte aplikaci, zastavte `inventory-api` (v Aspire dashboardu tlačítkem *Stop*).
2. Vytvořte objednávku → po ~30 s je `Rejected` („Sklad neodpověděl včas.“).
3. Spusťte sklad znovu → v auditním logu uvidíte `StockReservedV1`, pak `ReleaseStockV1` a `StockReleasedV1`.

## Kód

[OrderSagaTimeoutWorker](code:src/MiniShop.Web/BackgroundServices/OrderSagaTimeoutWorker.cs) ·
[OrderSaga](code:src/MiniShop.Domain/Orders/Saga/OrderSaga.cs) ·
[ReleaseStockHandler](code:src/MiniShop.Application/Inventory/ReleaseStockHandler.cs) ·
[testy ságy](code:tests/MiniShop.Tests/Domain/OrderSagaTests.cs)
