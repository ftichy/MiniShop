---
title: Modul Invoicing
summary: Faktury – vystaví se z událostí po zaplacení (karta) nebo po doručení (dobírka), číslo z nepřerušené řady, PDF přes QuestPDF, e-mail s PDF v příloze posílá Notifications.
order: 9
---

> **V jedné větě:** Invoicing je účetní ve vedlejší kanceláři: z existujících událostí si drží **kopii objednávek**,
> a jakmile ví „komu a co“ i „zaplaceno“, vystaví **fakturu** s číslem z nepřerušené řady a oznámí ji
> (`InvoiceIssuedV1`). Ordering ani Payments o něm nevědí. Běží ve službě **Web**, data má ve schématu `invoicing`.
> Vznikl v e-shop kroku 15.

Tok a diagram: [Vystavení faktury](../../toky/faktura.md).

## Součásti

| Součást | Co dělá | Kód |
| --- | --- | --- |
| **BillableOrder** | kopie objednávky: odběratel, adresa, řádky s cenou a slevou, doprava, celkem; zaplaceno (kdy, jak), zrušeno | [BillableOrder.cs](code:src/MiniShop.Domain/Invoicing/BillableOrder.cs) |
| **Invoice** | faktura – vlastní kopie všech údajů z doby vystavení, jednou vystavená se nemění | [Invoice.cs](code:src/MiniShop.Domain/Invoicing/Invoice.cs) |
| **InvoiceNumberSeries** | číselná řada roku (`2026000001`…), `Version` jako concurrency token | [InvoiceNumberSeries.cs](code:src/MiniShop.Domain/Invoicing/InvoiceNumberSeries.cs) |
| **InvoicingHandler** | plní kopii z událostí (upsert – zprávy mimo pořadí), vystaví fakturu + `InvoiceIssuedV1` v jedné transakci | [InvoicingHandler.cs](code:src/MiniShop.Application/Invoicing/InvoicingHandler.cs) |
| **InvoiceService** | PDF ke stažení (vlastník nebo admin), číslo faktury k objednávce, přehled pro admina | [InvoiceService.cs](code:src/MiniShop.Application/Invoicing/InvoiceService.cs) |
| **QuestPdfInvoiceRenderer** | PDF z faktury (QuestPDF, písmo Lato v knihovně – české znaky i v kontejneru) | [QuestPdfInvoiceRenderer.cs](code:src/MiniShop.Infrastructure/Invoicing/QuestPdfInvoiceRenderer.cs) |
| **InvoicingDbContext** | schéma `invoicing`: `BillableOrders` (+ řádky), `Invoices` (+ řádky), `InvoiceNumberSeries`, inbox, outbox | [InvoicingDbContext.cs](code:src/MiniShop.Infrastructure/Invoicing/InvoicingDbContext.cs) |
| **InvoiceDocuments** | most ve Webu: Notifications (`IInvoiceDocuments`) dostanou PDF do přílohy, aniž by Invoicing znaly | [InvoiceDocuments.cs](code:src/MiniShop.Web/Invoicing/InvoiceDocuments.cs) |
| **Stránky** | PDF `/Orders/{id}/Invoice` (odkaz na detailu objednávky), přehled `/Admin/Invoices` | [Orders/Invoice](code:src/MiniShop.Web/Pages/Orders/Invoice.cshtml.cs), [Admin/Invoices](code:src/MiniShop.Web/Pages/Admin/Invoices/Index.cshtml.cs) |

## Zprávy

| Směr | Zprávy |
| --- | --- |
| poslouchá (fronta `minishop.invoicing`) | `OrderPlacedV2` (komu a co), `PaymentSucceededV1` (zaplaceno kartou), `OrderCompletedV1` (doručeno = dobírka zaplacena), `OrderRejectedV1` + `OrderCancelledV1` (nefakturovat), `CustomerAnonymizedV1` |
| posílá | `InvoiceIssuedV1` (číslo, objednávka, částka – odebírá Notifications) |

## Pravidla

- **Faktura jen ze zaplacené objednávky** se známými údaji. Karta = `PaymentSucceededV1`, dobírka a objednávky
  z API = doručení. Platí první zpráva o zaplacení – doručení karetní objednávky druhou fakturu nevystaví.
- **Jedna faktura na objednávku** (unikátní `OrderId`), každé číslo jen jednou (unikátní `Number`).
- **Číselná řada bez děr:** číslo se přidělí ve **stejné transakci** jako faktura a zpráva `InvoiceIssuedV1`.
  Sekvence databáze by při rollbacku nechala díru. Dvě kopie Webu naráz → konflikt verze řady → výjimka →
  consumer zprávu zopakuje s dalším číslem.
- **Faktura se nemění.** Údaje jsou kopie z doby vystavení; PDF se skládá znovu jen kvůli vzhledu.
- **Zamítnutá nebo zrušená objednávka** před vystavením se nefakturuje (pozdní platbu vrátí sága).
  Zrušení **po** vystavení zatím řeší ručně – dobropis je e-shop krok 15B.
- **Smazaný účet:** kopie objednávek se smažou, **faktury zůstávají** (zákon je chce 10 let) – uvedeno v Ochraně osobních údajů.
- **Neplátce DPH** a fiktivní prodávající (`Invoicing:Seller`, výchozí v `InvoiceSeller.Default`) – výukový projekt.
- **Musí do zálohy** – faktury z událostí přestavět nejde (čísla). Schéma je v nočním `pg_dump` celé databáze.

## Proč samostatný modul

Faktura je jiný „jazyk“ než objednávka: neměnný doklad, číselná řada, zákonné lhůty. Kdyby ji vystavoval Ordering,
každá změna objednávky by se musela ptát „není už vyfakturovaná?“. Takhle Ordering jen oznamuje fakta a účetní si
z nich vezme, co potřebuje – stejně jako Notifications, Reporting a Reviews.
