---
title: Vystavení faktury
summary: Po zaplacení (karta) nebo doručení (dobírka) vystaví modul Invoicing fakturu s číslem z nepřerušené řady a Notifications pošle e-mail s PDF v příloze.
order: 17
---

> **Shrnutí:** Faktura vzniká **z událostí, které už existují** – Ordering ani Payments se kvůli ní nezměnily.
> Modul Invoicing si z `OrderPlacedV2` uloží, komu a co fakturovat, a jakmile přijde „zaplaceno“
> (`PaymentSucceededV1`, u dobírky `OrderCompletedV1`), vystaví fakturu a oznámí `InvoiceIssuedV1`.
> Notifications pak pošle e-mail s PDF v příloze.

Přirovnání: účetní ve vedlejší kanceláři sleduje nástěnku. Když tam visí „objednávka 42: Jan, myš, 200 Kč“
a „objednávka 42 zaplacena“, vezme další číslo z knihy faktur, vystaví fakturu a pošle ji poštou.

## Platba kartou

```mermaid
sequenceDiagram
    autonumber
    participant O as Ordering
    participant P as Payments
    participant I as Invoicing (schéma invoicing)
    participant N as Notifications
    participant W as Web (InvoiceDocuments)

    O-)I: OrderPlacedV2 (odběratel, řádky, doprava, celkem)
    I->>I: kopie objednávky (BillableOrder)
    P-)I: PaymentSucceededV1
    I->>I: zaplaceno → řada roku: další číslo
    Note over I: jedna transakce: faktura + číslo + InvoiceIssuedV1 v outboxu
    I-)N: InvoiceIssuedV1 (číslo, objednávka, částka)
    N->>W: IInvoiceDocuments.GetInvoiceAsync(objednávka)
    W->>I: InvoiceService – PDF (QuestPDF)
    N->>N: e-mail „Faktura … k objednávce č. 42“ + PDF v příloze
```

- **Dobírka a objednávky z API:** místo `PaymentSucceededV1` rozhodne `OrderCompletedV1` (zaplaceno při převzetí).
- **Zprávy mimo pořadí:** když platba předběhne vznik, kopie se založí jen s „zaplaceno“ a faktura vznikne,
  až dorazí `OrderPlacedV2`. Když `InvoiceIssuedV1` předběhne `OrderPlacedV2` v Notifications, e-mail se zkusí znovu.
- **PDF nejde přes RabbitMQ** – ve zprávě je jen číslo. Notifications si PDF vezmou přes rozhraní `IInvoiceDocuments`,
  které implementuje Web (oba moduly běží ve stejném procesu), stejně jako `ICustomerContacts` u připomínky košíku.

## Číselná řada a souběh

```mermaid
sequenceDiagram
    autonumber
    participant A as Web kopie 1
    participant DB as PostgreSQL (InvoiceNumberSeries)
    participant B as Web kopie 2

    A->>DB: načíst řadu 2026 (LastSequence 7, Version 7)
    B->>DB: načíst řadu 2026 (LastSequence 7, Version 7)
    A->>DB: UPDATE … SET LastSequence 8 WHERE Version = 7 + faktura 2026000008 ✔
    B->>DB: UPDATE … WHERE Version = 7 → 0 řádků ✘
    Note over B: ConcurrencyConflictException → celá transakce zpět (i faktura)<br/>consumer zprávu zopakuje → 2026000009
```

Proč ne sekvence databáze: ta číslo při rollbacku nevrátí a v řadě by zůstala díra. Zákon chce řadu nepřerušenou.

## Co vidí zákazník a admin

- **Detail objednávky** `/Orders/{id}`: „Faktura č. … – stáhnout PDF“ (`/Orders/{id}/Invoice`, cizí = 404).
- **E-mail** s PDF v příloze (šablona `OrderEmailTemplates.Invoice`).
- **Admin** `/Admin/Invoices`: nejnovější faktury s odkazem na PDF a objednávku.

Kód: [InvoicingHandler](code:src/MiniShop.Application/Invoicing/InvoicingHandler.cs),
[InvoiceEmailHandler](code:src/MiniShop.Application/Notifications/InvoiceEmailHandler.cs),
[QuestPdfInvoiceRenderer](code:src/MiniShop.Infrastructure/Invoicing/QuestPdfInvoiceRenderer.cs). Modul: [Invoicing](../architektura/moduly/invoicing.md).
