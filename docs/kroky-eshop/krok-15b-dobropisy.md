# E-shop krok 15B: Dobropisy

Faktura se po vystavení nesmí měnit. Když zaplacená objednávka s fakturou skončí **zrušením, zamítnutím nebo
vrácením**, modul Invoicing k ní vystaví **dobropis** – opravný doklad, který na fakturu odkazuje a celou její částku
vrací (faktura +1 250 Kč, dobropis −1 250 Kč, dohromady nula). Ordering, Payments ani Reporting se nezměnily.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md) (krok 15 – „Dobropis při zrušení/vrácení = krok 15B“).

## Jak to funguje

Přirovnání: účetní nevytrhne stránku z knihy faktur. Do druhé knihy dopíše „ruší se faktura č. 2026000042“
a pošle zákazníkovi kopii.

| Událost (existující) | Situace s fakturou | Text na dobropisu |
| --- | --- | --- |
| `OrderCancelledV1` | zaplaceno kartou a zrušeno před odesláním (dobírka fakturu před doručením nemá) | Vráceno na kartu |
| `OrderRejectedV1` | vzácný souběh – pozdní platba stihla fakturu dřív, než zamítnutí dorazilo | Vráceno na kartu |
| `ReturnCompletedV1` | vratka, kartou i dobírkou (`RefundedByCard`) | Vráceno na kartu / Vrátíme převodem na účet |

`InvoicingHandler` si na kopii objednávky (`BillableOrder`) poznamená **důvod** (`CreditReason`, platí první)
a text o vrácení peněz. Po každé zprávě platí pravidlo: **faktura existuje + důvod je znám + dobropis ještě není →
dobropis**. V jedné transakci vezme další číslo z řady dobropisů (`CreditNoteNumberSeries.Next` → `D2026000001`),
uloží `CreditNote` (kopie **faktury**, ne kopie objednávky – ta po smazání účtu zmizí) a do outboxu `CreditNoteIssuedV1`.
Notifications (`InvoiceEmailHandler`, stejný jako u faktury) pošle e-mail s PDF dobropisu v příloze.

Zprávy mimo pořadí: zrušení **před** fakturou = faktura nevznikne vůbec (jako v kroku 15). Vratka **před** fakturou
(teoreticky) = faktura a hned po ní dobropis. Peníze dál vrací sága a Payments – dobropis je jen doklad.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Invoicing/CreditNoteIssuedV1.cs` | událost „dobropis vystavený“ (číslo dobropisu, číslo faktury, objednávka, částka) |
| `src/MiniShop.Domain/Invoicing/CreditNote.cs` | dobropis (`Issue` z faktury) + `CreditNoteReason` (Cancelled, Rejected, Returned) |
| `src/MiniShop.Domain/Invoicing/CreditNoteNumberSeries.cs` | řada dobropisů roku, `Next()` → `D2026000001`, `Version` |
| `src/MiniShop.Infrastructure/Invoicing/Migrations/…_CreditNotes.cs` | tabulky `CreditNotes`, `CreditNoteLines`, `CreditNoteNumberSeries`, sloupce `BillableOrders.CreditReason` a `RefundNote` |
| `src/MiniShop.Web/Pages/Orders/CreditNote.cshtml(.cs)` | `/Orders/{id}/CreditNote` – PDF dobropisu, cizí = 404 |

## Změněné soubory

- `BillableOrder` – `CreditReason`, `RefundNote`, `MarkVoided(reason, refundNote)`, `MarkReturned(refundNote)`.
- `InvoicingHandler` – nová zpráva `ReturnCompletedV1`, dobropis ve stejné transakci; konstanty `RefundedToCard`, `RefundedByTransfer`.
- `InvoicingAbstractions` – `ICreditNoteRepository`, `ICreditNoteNumberSeriesRepository`, `IInvoicePdfRenderer.Render(CreditNote, …)`,
  `OrderDocumentsDto`, `InvoiceSummaryDto.CreditNoteNumber`.
- `InvoiceService` – `GetCreditNotePdfAsync`, `GetDocumentsAsync` (místo `GetNumberAsync`), přehled se sloupcem dobropisu.
- `QuestPdfInvoiceRenderer` – společný model dokladu; dobropis = nadpis „Dobropis“, „K faktuře“, důvod, záporné částky.
- `InvoicingDbContext`, `InvoicingConfigurations`, `InvoicingRepositories`, `InvoicingInfrastructure` – nové tabulky a repozitáře.
- Notifications: `IInvoiceDocuments.GetCreditNoteAsync`, `InvoiceEmailHandler` (i `CreditNoteIssuedV1`), `OrderEmailTemplates.CreditNote`;
  Web `InvoiceDocuments`, registrace v `ApplicationServiceCollectionExtensions`.
- Stránky: detail objednávky (odkaz na dobropis), `/Admin/Invoices` (sloupec Dobropis), `/Privacy` (dobropisy 10 let).
- Testy: `OrderServiceTests` (6 testů `Dobropis_*`, `NotifierEvent` zná `credit-note`), `InvoiceTests`, `InvoicePageTests`,
  `AuthorizationMatrixTests`.
- Příručka: modul Invoicing, tok Vystavení faktury a dobropisu, Notifications, E-maily, vrácení zboží, zrušení, moduly, slovníček, historie.

## Rozhodnutí v tomto kroku

- **Dobropis z kopie faktury**, ne z kopie objednávky – údaje musí sedět s fakturou a kopie objednávky může být po
  smazání účtu pryč.
- **Vlastní tabulka řady** `CreditNoteNumberSeries`, ne sloupec „druh“ v `InvoiceNumberSeries`: změna primárního klíče
  existující tabulky by při postupném nasazení (stará a nová verze běží chvíli souběžně) rozbila starou verzi, která
  čte řadu jen podle roku. Nová tabulka a nullable sloupce jsou kompatibilní (expand).
- **Prefix `D`** – na první pohled je jasné, že jde o dobropis, a čísla se s fakturami nemohou potkat.
- **Žádný nový vstupní kontrakt** – stačí existující `OrderCancelledV1`, `OrderRejectedV1`, `ReturnCompletedV1`.
- **E-mail přes stávající `InvoiceEmailHandler`** (druhé rozhraní) – stejné hledání příjemce i čekání na `OrderPlacedV2`.
- **Platí první důvod** – pozdní zpráva (např. zamítnutí po zrušení) text dobropisu nezmění.

## Známá omezení

- Jen **celý** dobropis – částečné vrácení (jedna položka z objednávky) MiniShop nezná.
- Objednávky zrušené nebo vrácené **před nasazením 15B** dobropis nedostanou (nic se zpětně nedohledává).
- Neplátce DPH – dobropis není „opravný daňový doklad“ s rozpisem daně.
- E-mail s dobropisem je „at-least-once“ jako ostatní e-maily.

## Jak ověřit

- `dotnet test MiniShop.slnx` – `OrderServiceTests` (Dobropis_*), `InvoiceTests`, `InvoicePageTests`, `AuthorizationMatrixTests`.
- Lokálně (AppHost + Mailpit): objednávka kartou → *Zaplatit* → faktura → *Zrušit* → za pár sekund e-mail „Dobropis …“
  s PDF, na detailu objednávky odkaz *Dobropis č. D… – stáhnout PDF*; v `/Admin/Invoices` sloupec Dobropis.

**Nasazeno 9. 11. 2026** (PR #75, postupné nasazení bez ručního kroku na VM, migrace `CreditNotes` při startu).
V produkci ověřeno: zaplacená objednávka kartou zrušená → dobropis `D2026…`, e-mail s PDF v příloze, odkaz na detailu
objednávky a sloupec Dobropis v `/Admin/Invoices`.

## Vyzkoušejte sami

1. Objednejte kartou, zaplaťte, počkejte na fakturu a objednávku zrušte. Na detailu přibude *Dobropis č. D2026000001*;
   v PDF jsou částky se znaménkem minus a odkaz na fakturu.
2. Dobírka: nechte objednávku doručit (Expedice → *Doručeno*), požádejte o vratku a v *Vratky* ji přijměte →
   dobropis s textem „Vrátíme převodem na účet“.
3. Zrušte objednávku ještě **před** zaplacením – nevznikne faktura ani dobropis.
4. Jako jiný zákazník zkuste `/Orders/<cizí číslo>/CreditNote` → 404.
