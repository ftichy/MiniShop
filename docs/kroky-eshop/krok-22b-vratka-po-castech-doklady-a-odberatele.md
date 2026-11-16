# E-shop krok 22B: Vratka po částech – dobropisy, e-maily, tržby, hodnocení

Druhá ze tří částí issue #119 (vratka po částech, zavádí se **od konce**):

| Část | Co | Kdo posílá nové zprávy |
| --- | --- | --- |
| 22A | sklad a Payments umí vratku po částech | nikdo (jen testy) |
| **22B (tento)** | dobropisy, e-maily, tržby, hodnocení | nikdo (jen testy) |
| 22C | Ordering – výběr položek, sága pro každou vratku, obrazovky | Ordering – funkce se zapne |

Ordering zatím posílá jen V1 (vratka celé objednávky), takže v produkci se nic nemění. Odběratelé se ale už naučili nové
zprávy – až je Ordering v 22C začne posílat, nebude žádný mezistav, kdy by částečná vratka vystavila celý dobropis.

## Jak to funguje

Nové zprávy Orderingu (kontrakty, posílat je začne 22C):

- `ReturnItemV1` – položka vratky: produkt, název, kusy, cena za kus, podíl slevy z kuponu, částka.
- `ReturnRequestedV2` / `ReturnRejectedV2` – jako V1 + `ReturnNumber` (1, 2, … v objednávce), žádost i s položkami.
- `ReturnCompletedV2` – položky, `ShippingRefund` (dnes vždy 0), `Amount`, `RefundedByCard` a `FullyReturnedProductIds`
  (produkty, ze kterých si zákazník po této vratce nenechal ani kus – počítá je Ordering, odběratelé nemusí znát množství).

Odběratelé:

1. **Invoicing** – `ReturnCompletedV2` → `CreditNote.IssueForReturn`: hlavička z faktury, řádky ze zprávy. K jedné faktuře
   tak může být víc dobropisů; unikátní je `OrderId + ReturnNumber` (celý dobropis má 0). Pojistka: součet dobropisů nikdy
   nepřesáhne fakturu. Číslo z řady + dobropis + `CreditNoteIssuedV1` v jedné transakci jako dřív.
2. **Notifications** – e-maily „Vratka 2 k objednávce … – pošlete nám zboží“, „… nebyla přijata“, „… je vyřízená“ se
   seznamem vracených položek. E-mail s dobropisem hledá PDF podle **čísla dobropisu** (`IInvoiceDocuments.GetCreditNoteAsync`).
3. **Reporting** – `SalesOrder.ApplyReturn` (tabulky `SalesOrderReturns` + `SalesOrderReturnLines`): stav zůstává Completed,
   tržba = cena − vrácené (`Revenue`), top produkty bez vrácených kusů (`KeptLines`). `PaymentRefundedV2` jde do „Vráceno“.
4. **Reviews** – `PurchasedOrder.MarkProductsReturned` (tabulka `PurchasedReturnedProducts`): produkt vrácený celý už z této
   objednávky hodnotit nejde, když si zákazník nechal aspoň kus, smí.

Web: detail objednávky ukáže všechny dobropisy, `/Admin/Invoices` je má pod sebou, stažení `/Orders/{id}/CreditNote/{číslo}`
(bez čísla = poslední – staré odkazy fungují). PDF dobropisu za vratku má „Vratka: N“ a nemá řádek dopravy za 0 Kč.

## Nové soubory

- Kontrakty `ReturnItemV1`, `ReturnRequestedV2`, `ReturnRejectedV2`, `ReturnCompletedV2` (`src/MiniShop.Contracts/Orders/`)
- Migrace `PartialReturns` pro Invoicing (sloupec `ReturnNumber`, unikátní index objednávka + číslo vratky místo objednávky),
  Reporting (`SalesOrderReturns`, `SalesOrderReturnLines`) a Reviews (`PurchasedReturnedProducts`)

## Změněné soubory

`CreditNote` (`ReturnNumber`, `IsPartial`, `IssueForReturn`), `InvoicingHandler` (V2, `NextCreditNoteNumberAsync`),
`ICreditNoteRepository` (`GetByNumberAsync`, `GetForOrderAsync`, `ExistsForReturnAsync`, `GetCreditedAmountAsync`, víc dobropisů
v `GetByOrdersAsync`), `InvoiceService` + DTO (`CreditNotes`, `CreditNoteNumbers`), `QuestPdfInvoiceRenderer`,
`IInvoiceDocuments` + `InvoiceDocuments` (podle čísla), `InvoiceEmailHandler`, `OrderNotificationsHandler` + `OrderEmailTemplates`
(`PartialReturn*`, text e-mailu dobropisu „opravuje fakturu“), `SalesOrder` + `SalesProjectionHandler` + `SalesReportService`,
`PurchasedOrder` + `PurchaseProjectionHandler` + `PurchaseRepository`, stránky `Orders/CreditNote`, `Orders/Detail`,
`Admin/Invoices`, registrace (`ApplicationServiceCollectionExtensions`, testy), příručka (moduly Invoicing, Notifications,
Reporting, Reviews, toky Vrácení zboží a Faktura, observabilita, historie), CLAUDE.md.

## Rozhodnutí

- **Řádky dobropisu ze zprávy, hlavička z faktury.** Faktura nezná ID produktů (jen názvy) – kolik a za kolik se vrací,
  ví Ordering. Hlavička (jméno, adresa) z faktury, protože kopie objednávky po smazání účtu zmizí.
- **Index `OrderId + ReturnNumber` v jednom nasazení:** starší verze kódu zapisuje vždy `ReturnNumber = 0`, takže ji nový
  index chrání stejně jako starý unikátní `OrderId`. Migrace nejdřív vytvoří nový index, pak smaže starý.
- **Celý dobropis po vratkách po částech nevznikne** – Ordering ho podle rozhodnutí z issue #119 nepošle (celá objednávka
  přes V1 jen jako první vratka). Kdyby přesto přišel, raději nic než dobropis nad fakturu.
- **Reviews: vlastní tabulka místo příznaku u produktu** – `ApplyPlaced` (vznik i snímek) seznam produktů přepisuje; příznak
  by pozdní snímek smazal. Seznam vrácených se jen přidává, takže nezáleží na pořadí zpráv.
- **„Vrácený celý“ počítá Ordering** (`FullyReturnedProductIds`) – Reviews množství nezná a nemusí.
- **E-mail o vrácení peněz kartou** u vratky po částech nese „je vyřízená“ (`ReturnCompletedV2`); na `PaymentRefundedV2`
  Notifications zvlášť nepíšou.

## Známá omezení

- Přestavění přehledu tržeb (`OrderSnapshotV1`) vratky po částech nezná – po přestavění se tržba ukáže bez nich. Snímek
  s vratkami doplní 22C.
- Dobropis k vratce, ke které ještě není faktura (zprávy mimo pořadí), se jen opakuje (consumer 5× s backoffem) – prakticky
  nenastane, vratka přichází dny po doručení a faktuře.
- Inbox Notifications zná jen ID zprávy – kdyby Ordering poslal stejnou vratku dvakrát s jiným ID, přišly by dva e-maily
  (dobropis, tržba i hodnocení se nezdvojí). Ordering posílá každou událost jednou přes outbox.

## Jak ověřit

- `dotnet test` – `InvoiceTests.DobropisZaVratkuPoCastech_*`, `SalesOrderTests.VratkaPoCastech_*`, `ReviewTests.Nakup_VratkaPoCastech_*`,
  `OrderServiceTests.VratkaPoCastech_DobropisEmailTrzbaHodnoceni` (celá cesta přes 4 moduly nad PostgreSQL),
  `IntegrationContractTests.VratkaPoCastechOrderingV2_*`, `AuthorizationMatrixTests` (`/Orders/1/CreditNote/{číslo}`).
- Produkce: chování se nemění. Stačí ověřit, že detail objednávky s dobropisem (zrušená zaplacená) ukazuje dobropis a PDF
  jde stáhnout, a že přehled faktur ukazuje číslo dobropisu.

## Vyzkoušejte sami

1. V `OrderServiceTests.VratkaPoCastech_DobropisEmailTrzbaHodnoceni` změňte částku druhé vratky na 150 Kč – `IssueForReturn`
   odmítne, protože by dobropisy přesáhly fakturu.
2. V `ReviewTests.Nakup_VratkaPoCastech_*` dejte `MarkProductsReturned` až za `ApplyPlaced` – výsledek je stejný (pořadí nevadí).
