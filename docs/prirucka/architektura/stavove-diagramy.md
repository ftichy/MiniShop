---
title: Stavové diagramy
summary: Životní cyklus objednávky a ságy – které přechody jsou povolené a co je spouští.
order: 6
---

> **V jedné větě:** Objednávka i sága jsou **stavové automaty** – mají pevně dané stavy a přechody mezi nimi.
> Nepovolený přechod (vyřídit zamítnutou objednávku) doména odmítne.

## Objednávka (`Order`)

```mermaid
stateDiagram-v2
    [*] --> Pending : zákazník odeslal formulář
    Pending --> Confirmed : sklad zarezervoval (sága)
    Pending --> Rejected : nedostatek zboží / sklad neodpověděl včas
    Confirmed --> Completed : obsluha vyřídila
    Confirmed --> Cancelled : obsluha zrušila → sága vrátí zboží
    Rejected --> [*]
    Completed --> [*]
    Cancelled --> [*]
```

| Stav | Význam | Událost při vstupu |
| --- | --- | --- |
| `Pending` | Přijatá, čeká na rezervaci skladu | `OrderPlaced` |
| `Confirmed` | Zboží zarezervované | `OrderConfirmed` |
| `Rejected` | Zamítnutá, důvod v `RejectionReason` | `OrderRejected` |
| `Completed` | Vyřízená | `OrderCompleted` |
| `Cancelled` | Zrušená | `OrderCancelled` |

Pravidla hlídá přímo entita [Order](code:src/MiniShop.Domain/Orders/Order.cs) – metody `Confirm`, `Reject`,
`Complete`, `Cancel` nejdřív zkontrolují stav (`EnsureStatus`) a při chybě vyhodí `DomainException`.
O `Confirmed`/`Rejected` **nerozhoduje uživatel**, ale [sága](../eda/saga.md).

## Sága objednávky (`OrderSaga`)

```mermaid
stateDiagram-v2
    [*] --> AwaitingReservation : OrderPlaced → pošle ReserveStockV1
    AwaitingReservation --> Reserved : StockReservedV1 → potvrdí objednávku
    AwaitingReservation --> Rejected : StockReservationFailedV1 → zamítne
    AwaitingReservation --> Rejected : timeout (30 s) → zamítne
    Rejected --> ReleasingStock : StockReservedV1 pozdě → pošle ReleaseStockV1
    Reserved --> Completed : OrderCompleted
    Reserved --> ReleasingStock : OrderCancelled → pošle ReleaseStockV1
    ReleasingStock --> Released : StockReleasedV1
    Completed --> [*]
    Released --> [*]
```

Sága je „deník“ procesu: z tabulky `OrderSagas` je hned vidět, kde objednávka visí. Kód:
[OrderSaga.cs](code:src/MiniShop.Domain/Orders/Saga/OrderSaga.cs).

### Na co si dát pozor

- **Zpráva, která do stavu nepatří, nic neudělá.** Duplicitní `StockReservedV1` ve stavu `Reserved` vrátí akci
  `None`. To je druhá pojistka vedle inboxu.
- **Pozdní odpověď skladu.** Když timeout objednávku zamítne a sklad pak přece jen zarezervuje, sága zboží vrátí
  (`Rejected → ReleasingStock`). Bez toho by zboží zůstalo navždy „odložené“. Viz
  [Timeout a kompenzace](../toky/timeout-a-kompenzace.md).
- **Souběh.** Timeout a odpověď skladu můžou přijít naráz. Sága má `Version` (*concurrency token*) – druhý zápis
  selže a zkusí se znovu nad aktuálním stavem.

## Na internetu

- [Mermaid: State diagrams](https://mermaid.js.org/syntax/stateDiagram.html)
- [Konečný automat (Wikipedie)](https://cs.wikipedia.org/wiki/Kone%C4%8Dn%C3%BD_automat)
