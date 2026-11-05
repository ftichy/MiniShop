---
title: Modul Reporting
summary: Přehled tržeb pro admina – čtecí model (CQRS) z událostí objednávek a plateb, vlastní schéma reporting, přestavění ze snímků.
order: 7
---

> **V jedné větě:** Reporting si z integračních událostí, které už posílají Ordering a Payments, skládá vlastní
> tabulku pro dotazy admina (tržby za den, nejprodávanější produkty, počty podle stavu) – na objednávky se nikdy
> neptá napřímo. Běží ve službě **Web**, data má ve schématu `reporting`. Vzniklo v e-shop kroku 11.

Princip a diagramy: [CQRS – přehled tržeb](../../eda/cqrs.md).

## Součásti

| Součást | Co dělá | Kód |
| --- | --- | --- |
| **SalesOrder** | řádek čtecího modelu: datum, stav (zjednodušený), částky, řádky; stav jen dopředu | [SalesOrder.cs](code:src/MiniShop.Domain/Reporting/SalesOrder.cs) |
| **SalesProjectionHandler** | projekce – upsert z událostí a ze snímků | [SalesProjectionHandler.cs](code:src/MiniShop.Application/Reporting/SalesProjectionHandler.cs) |
| **SalesReportService** | čtecí strana (součty po dnech, top 10, počty) a přestavění | [SalesReportService.cs](code:src/MiniShop.Application/Reporting/SalesReportService.cs) |
| **ReportingDbContext** | schéma `reporting`: `SalesOrders`, `SalesOrderLines`, inbox, outbox | [ReportingDbContext.cs](code:src/MiniShop.Infrastructure/Reporting/ReportingDbContext.cs) |
| **SalesReportOnStartupWorker** | prázdný přehled po startu → požádat o snímky | [SalesReportOnStartupWorker.cs](code:src/MiniShop.Web/BackgroundServices/SalesReportOnStartupWorker.cs) |
| **Stránka Přehled** | `/Admin/Sales` (jen admin), tlačítko Přestavět | [Index.cshtml.cs](code:src/MiniShop.Web/Pages/Admin/Sales/Index.cshtml.cs) |

## Zprávy

| Směr | Zprávy |
| --- | --- |
| poslouchá (fronta `minishop.reporting`) | `OrderPlacedV2`, `OrderConfirmedV1`, `OrderRejectedV1`, `OrderCancelledV1`, `OrderCompletedV1`, `PaymentRefundedV1`, `OrderSnapshotV1`, `ReturnCompletedV1` (e-shop krok 12 – vrácená se do tržby nepočítá) |
| posílá (příkaz pro Ordering) | `RebuildSalesReportV1` → Ordering odpoví `OrderSnapshotV1` pro každou objednávku |

## Co přehled počítá

- **Tržba** = objednávky, které nejsou zamítnuté ani zrušené, včetně dopravy (i ty, které ještě čekají na sklad
  nebo platbu). Den podle českého času vzniku objednávky.
- **Top 10 produktů** za 30 dní podle kusů (stejné pravidlo – jen platné objednávky).
- **Vráceno** – součet `PaymentRefundedV1` za 30 dní (po přestavění 0 – platby zná jen Payments).
- **Počty podle stavu** – zjednodušené stavy: čeká, potvrzená (i balená a odeslaná), doručená, zamítnutá, zrušená.
