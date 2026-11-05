---
title: Moduly
summary: Ordering, Inventory, Journeys, Accounts, Notifications, Payments, Reporting a Reviews – business oblasti s vlastními daty, které se navzájem neznají.
order: 3
---

> **V jedné větě:** Modul je samostatné „oddělení“ firmy s vlastními daty a vlastními pravidly. Moduly spolu
> mluví jen zprávami – nikdy si nesahají do databáze ani do kódu.

## Přehled

```mermaid
flowchart LR
    subgraph ord["Ordering (služba Web)"]
        o1[Objednávky]
        o2[Sága objednávky]
        o3[Kopie katalogu]
    end
    subgraph inv["Inventory (služba Inventory.Api)"]
        i1[Produkty]
        i2[Rezervace zboží]
    end
    subgraph jou["Journeys (služba Web)"]
        j1[Proud událostí formuláře]
        j2[Projekce: průběh, přehrání]
    end

    o2 -->|"ReserveStockV1, ReleaseStockV1"| i2
    i2 -->|"StockReservedV1, StockReservationFailedV1, StockReleasedV1"| o2
    i1 -->|"ProductUpdatedV1 + V2, StockAvailabilityChangedV1, ProductRemovedV1"| o3
    o3 -->|SyncCatalogV1| i1
    subgraph acc["Accounts (služba Web)"]
        a1[Uživatelské účty, přihlášení]
    end
    subgraph ntf["Notifications (služba Web)"]
        n1[E-maily zákazníkovi]
    end
    subgraph pay["Payments (služba Web)"]
        p1[Platby kartou]
    end
    o2 -->|"RequestPaymentV1, RefundPaymentV1"| p1
    p1 -->|"PaymentSucceededV1, PaymentFailedV1"| o2
    o1 -->|"OrderPlacedV2, OrderConfirmedV1, …, CustomerAnonymizedV1"| n1
    subgraph rep["Reporting (služba Web)"]
        r1[Přehled tržeb – čtecí model]
    end
    o1 -->|"OrderPlacedV2, OrderConfirmedV1, …, OrderSnapshotV1"| r1
    r1 -->|RebuildSalesReportV1| o1
    subgraph rev["Reviews (služba Web)"]
        v1[Hodnocení + kopie nákupů]
    end
    o1 -->|"OrderPlacedV2, OrderCompletedV1, ReturnCompletedV1, OrderSnapshotV2"| v1
    v1 -->|"ProductRatingChangedV1, SyncPurchasesV1"| o3

    jou -.-|"spojené jen číslem objednávky (zapíše UI)"| ord

    click o2 "/Docs/eda/saga"
    click o3 "/Docs/eda/kopie-katalogu"
    click j1 "/Docs/eda/proud-udalosti"
    click o1 "/Docs/architektura/moduly/ordering"
    click i1 "/Docs/architektura/moduly/inventory"
    click i2 "/Docs/architektura/moduly/inventory"
    click j2 "/Docs/architektura/moduly/journeys"
    click a1 "/Docs/architektura/moduly/accounts"
    click n1 "/Docs/architektura/moduly/notifications"
    click p1 "/Docs/architektura/moduly/payments"
    click r1 "/Docs/architektura/moduly/reporting"
    click v1 "/Docs/architektura/moduly/reviews"
```

| Modul | Odpovídá za | Databáze | Kde v kódu |
| --- | --- | --- | --- |
| [Ordering](ordering.md) | objednávky, jejich stavy, ságu, kopii katalogu, košík, pokladnu, expedici | schéma `ordering` | `Domain/Orders`, `Application/Ordering`, `Infrastructure/Ordering` |
| [Inventory](inventory.md) | produkty, skladové zásoby, rezervace | schéma `inventory` | `Domain/Inventory`, `Application/Inventory`, `Infrastructure/Inventory` |
| [Journeys](journeys.md) | co uživatel dělal ve formuláři objednávky | schéma `journeys` | `Domain/Journeys`, `Application/Journeys`, `Infrastructure/Journeys` |
| [Accounts](accounts.md) | uživatelské účty, registrace a přihlášení (ASP.NET Core Identity) | schéma `accounts` | `Infrastructure/Accounts`, stránky `Pages/Account` ve Webu |
| [Notifications](notifications.md) | e-maily zákazníkovi podle událostí objednávky (e-shop krok 7) – jen poslouchá | schéma `notifications` | `Domain/Notifications`, `Application/Notifications`, `Infrastructure/Notifications` |
| [Payments](payments.md) | platby kartou přes bránu – účastník ságy, podepsaný webhook, vrácení peněz (e-shop krok 8) | schéma `payments` | `Domain/Payments`, `Application/Payments`, `Infrastructure/Payments` |
| [Reporting](reporting.md) | přehled tržeb pro admina – čtecí model (CQRS) z událostí, přestavění ze snímků (e-shop krok 11) | schéma `reporting` | `Domain/Reporting`, `Application/Reporting`, `Infrastructure/Reporting` |
| [Reviews](reviews.md) | hodnocení produktů – hodnotí jen ten, kdo zboží dostal (kopie nákupů z událostí), souhrn do výlohy (e-shop krok 14) | schéma `reviews` | `Domain/Reviews`, `Application/Reviews`, `Infrastructure/Reviews` |

## Pravidla hranic

1. **Žádné reference mezi moduly** v žádné vrstvě. Hlídá to test
   [ModuleBoundariesTests](code:tests/MiniShop.Tests/Architecture/ModuleBoundariesTests.cs) – kdo z pohodlnosti
   sáhne z objednávek do skladu, neprojde mu build v CI.
2. **Vlastní databáze** – žádné sdílené tabulky. Ordering zná název a cenu produktu jen ze své
   [kopie katalogu](../../eda/kopie-katalogu.md).
3. **Vlastní outbox a inbox v databázi modulu** – jinak by zpráva nebyla v téže transakci jako data.
4. **Vlastní rozhraní pro ukládání a odesílání** (`IOrderingUnitOfWork`, `IInventoryMessagePublisher`…).
   Handler modulu nikdy nebere obecné `IUnitOfWork` – nemohl by omylem uložit do cizí databáze.
5. **Společné je jen jádro** (outbox, inbox, bus) a **kontrakty**. Jádro nezná žádný modul.

## Modulární monolit → samostatné služby

V kroku 10A byly všechny moduly v jednom procesu (*modulární monolit*), ale už s oddělenými databázemi a komunikací
přes zprávy. V kroku 10B se Inventory „odstěhoval“ do vlastní služby – a protože hranice už byly čisté, šlo hlavně
o změnu hostitele, ne o přepis kódu.

> **Přirovnání:** Nejdřív si oddělení rozdělí kanceláře v jedné budově a domluví se, že si budou posílat jen
> oficiální poštu. Až potom se jedno z nich může přestěhovat do jiné budovy, aniž by se změnil způsob práce.

**Proč nezačít rovnou mikroslužbami?** Hranice se na začátku často odhadnou špatně. V monolitu se dají
levně posunout; mezi službami už je to drahé (síť, nasazení, verze kontraktů).

## Na internetu

- [Kamil Grzybek: Modular Monolith Primer](https://www.kamilgrzybek.com/blog/posts/modular-monolith-primer)
- [Martin Fowler: MonolithFirst](https://martinfowler.com/bliki/MonolithFirst.html)
- [Martin Fowler: Bounded Context](https://martinfowler.com/bliki/BoundedContext.html)
