---
title: Modul Invoicing
summary: Faktury a dobropisy – faktura z událostí po zaplacení (karta) nebo po doručení (dobírka), dobropis po zrušení či vrácení, čísla z nepřerušených řad, PDF přes QuestPDF, e-mail s PDF v příloze posílá Notifications.
order: 9
---

> **V jedné větě:** Invoicing je účetní ve vedlejší kanceláři: z existujících událostí si drží **kopii objednávek**,
> a jakmile ví „komu a co“ i „zaplaceno“, vystaví **fakturu** s číslem z nepřerušené řady a oznámí ji
> (`InvoiceIssuedV1`). Ordering ani Payments o něm nevědí. Běží ve službě **Web**, data má ve schématu `invoicing`.
> Vznikl v e-shop kroku 15. Od kroku 15B k faktuře zrušené, zamítnuté nebo vrácené objednávky vystaví **dobropis**
> (`CreditNoteIssuedV1`) – fakturu nepřepisuje.

Tok a diagram: [Vystavení faktury a dobropisu](../../toky/faktura.md).

## Součásti

| Součást | Co dělá | Kód |
| --- | --- | --- |
| **BillableOrder** | kopie objednávky: odběratel, adresa, řádky s cenou a slevou, doprava, celkem; zaplaceno (kdy, jak), zrušeno | [BillableOrder.cs](code:src/MiniShop.Domain/Invoicing/BillableOrder.cs) |
| **Invoice** | faktura – vlastní kopie všech údajů z doby vystavení, jednou vystavená se nemění | [Invoice.cs](code:src/MiniShop.Domain/Invoicing/Invoice.cs) |
| **InvoiceNumberSeries** | číselná řada roku (`2026000001`…), `Version` jako concurrency token | [InvoiceNumberSeries.cs](code:src/MiniShop.Domain/Invoicing/InvoiceNumberSeries.cs) |
| **CreditNote** | dobropis (krok 15B) – kopie faktury s důvodem (`CreditNoteReason`: zrušeno, zamítnuto, vráceno) a textem o vrácení peněz; jeden na objednávku, nemění se | [CreditNote.cs](code:src/MiniShop.Domain/Invoicing/CreditNote.cs) |
| **CreditNoteNumberSeries** | vlastní řada dobropisů (`D2026000001`…), stejný princip jako řada faktur | [CreditNoteNumberSeries.cs](code:src/MiniShop.Domain/Invoicing/CreditNoteNumberSeries.cs) |
| **InvoicingHandler** | plní kopii z událostí (upsert – zprávy mimo pořadí), vystaví fakturu + `InvoiceIssuedV1` v jedné transakci; po zrušení/zamítnutí/vrácení dobropis + `CreditNoteIssuedV1` | [InvoicingHandler.cs](code:src/MiniShop.Application/Invoicing/InvoicingHandler.cs) |
| **InvoiceService** | PDF faktury i dobropisu ke stažení (vlastník nebo admin), čísla dokladů k objednávce, přehled pro admina | [InvoiceService.cs](code:src/MiniShop.Application/Invoicing/InvoiceService.cs) |
| **QuestPdfInvoiceRenderer** | PDF faktury a dobropisu – stejný layout, dobropis s odkazem na fakturu a zápornými částkami (QuestPDF, písmo Lato v knihovně – české znaky i v kontejneru) | [QuestPdfInvoiceRenderer.cs](code:src/MiniShop.Infrastructure/Invoicing/QuestPdfInvoiceRenderer.cs) |
| **InvoicingDbContext** | schéma `invoicing`: `BillableOrders` (+ řádky), `Invoices` (+ řádky), `InvoiceNumberSeries`, `CreditNotes` (+ řádky), `CreditNoteNumberSeries`, inbox, outbox | [InvoicingDbContext.cs](code:src/MiniShop.Infrastructure/Invoicing/InvoicingDbContext.cs) |
| **InvoiceDocuments** | most ve Webu: Notifications (`IInvoiceDocuments`) dostanou PDF faktury nebo dobropisu do přílohy, aniž by Invoicing znaly | [InvoiceDocuments.cs](code:src/MiniShop.Web/Invoicing/InvoiceDocuments.cs) |
| **Stránky** | PDF `/Orders/{id}/Invoice` a `/Orders/{id}/CreditNote` (odkazy na detailu objednávky), přehled `/Admin/Invoices` se sloupcem Dobropis | [Orders/Invoice](code:src/MiniShop.Web/Pages/Orders/Invoice.cshtml.cs), [Orders/CreditNote](code:src/MiniShop.Web/Pages/Orders/CreditNote.cshtml.cs), [Admin/Invoices](code:src/MiniShop.Web/Pages/Admin/Invoices/Index.cshtml.cs) |

## Zprávy

| Směr | Zprávy |
| --- | --- |
| poslouchá (fronta `minishop.invoicing`) | `OrderPlacedV2` (komu a co), `PaymentSucceededV1` (zaplaceno kartou), `OrderCompletedV1` (doručeno = dobírka zaplacena), `OrderRejectedV1` + `OrderCancelledV1` (před fakturou nefakturovat, po ní dobropis), `ReturnCompletedV1` (dobropis, krok 15B), `CustomerAnonymizedV1` |
| posílá | `InvoiceIssuedV1` (číslo, objednávka, částka), `CreditNoteIssuedV1` (číslo dobropisu, číslo faktury, částka) – obě odebírá Notifications |

## Pravidla

- **Faktura jen ze zaplacené objednávky** se známými údaji. Karta = `PaymentSucceededV1`, dobírka a objednávky
  z API = doručení. Platí první zpráva o zaplacení – doručení karetní objednávky druhou fakturu nevystaví.
- **Jedna faktura na objednávku** (unikátní `OrderId`), každé číslo jen jednou (unikátní `Number`).
- **Číselná řada bez děr:** číslo se přidělí ve **stejné transakci** jako faktura a zpráva `InvoiceIssuedV1`.
  Sekvence databáze by při rollbacku nechala díru. Dvě kopie Webu naráz → konflikt verze řady → výjimka →
  consumer zprávu zopakuje s dalším číslem.
- **Faktura se nemění.** Údaje jsou kopie z doby vystavení; PDF se skládá znovu jen kvůli vzhledu.
- **Zamítnutá nebo zrušená objednávka** před vystavením se nefakturuje (pozdní platbu vrátí sága).
- **Dobropis (krok 15B):** existuje-li faktura a objednávka skončí zrušením (`OrderCancelledV1`), zamítnutím
  (`OrderRejectedV1` – vzácný souběh s pozdní platbou) nebo vrácením (`ReturnCompletedV1`), vznikne **jeden** dobropis
  na celou částku faktury. Skládá se z **kopie faktury** (kopie objednávky po smazání účtu nemusí existovat).
  Text o vrácení peněz: „Vráceno na kartu“, u vratky dobírky „Vrátíme převodem na účet“. Platí první důvod.
  Vratka dřív než faktura (zprávy mimo pořadí) = faktura a hned po ní dobropis. Peníze dál vrací sága / Payments
  – dobropis je jen doklad.
- **Řada dobropisů** je vlastní tabulka `CreditNoteNumberSeries`, ne sloupec „druh“ v řadě faktur: změna klíče
  existující tabulky by při postupném nasazení rozbila starší verzi kódu (expand → contract).
- **Smazaný účet:** kopie objednávek se smažou, **faktury a dobropisy zůstávají** (zákon je chce 10 let) – uvedeno v Ochraně osobních údajů.
- **Neplátce DPH** a fiktivní prodávající (`Invoicing:Seller`, výchozí v `InvoiceSeller.Default`) – výukový projekt.
- **Musí do zálohy** – faktury a dobropisy z událostí přestavět nejde (čísla). Schéma je v nočním `pg_dump` celé databáze.

## Proč samostatný modul

Faktura je jiný „jazyk“ než objednávka: neměnný doklad, číselná řada, zákonné lhůty. Kdyby ji vystavoval Ordering,
každá změna objednávky by se musela ptát „není už vyfakturovaná?“. Takhle Ordering jen oznamuje fakta a účetní si
z nich vezme, co potřebuje – stejně jako Notifications, Reporting a Reviews.
