# E-shop krok 15: Faktury (PDF)

Zaplacená objednávka dostane **fakturu** – PDF ke stažení na detailu objednávky a v příloze e-mailu. Vystavuje ji nový
modul **Invoicing** jen z událostí, které už existují; Ordering ani Payments se nezměnily.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md) („kam dál“ – faktura PDF).

## Jak to funguje

Přirovnání: účetní ve vedlejší kanceláři sleduje nástěnku. Když tam visí „objednávka 42: Jan, myš, 200 Kč“
a „objednávka 42 zaplacena“, vezme další číslo z knihy faktur, vystaví fakturu a pošle ji poštou.

| Událost | Invoicing |
| --- | --- |
| `OrderPlacedV2` | kopie objednávky `BillableOrder` (odběratel, adresa, řádky s cenou a slevou, doprava, celkem) |
| `PaymentSucceededV1` | zaplaceno kartou |
| `OrderCompletedV1` | doručeno = dobírka / API zaplaceno při převzetí (u karty už nic) |
| `OrderRejectedV1`, `OrderCancelledV1` | nefakturovat (pozdní platbu vrátí sága) |
| `CustomerAnonymizedV1` | smazat kopie objednávek, faktury zůstávají |

Jakmile kopie zná „komu a co“ i „zaplaceno“, handler v **jedné transakci**: vezme další číslo řady roku
(`InvoiceNumberSeries.Next` → `2026000001`), uloží `Invoice` (vlastní kopie údajů, už se nemění) a do outboxu
`InvoiceIssuedV1`. Notifications (`InvoiceEmailHandler`) pošle e-mail s PDF v příloze – PDF si vezme přes
`IInvoiceDocuments`, které implementuje Web (`InvoiceDocuments` nad `InvoiceService`), takže moduly se neznají a PDF
nejde přes RabbitMQ. PDF skládá `QuestPdfInvoiceRenderer` (QuestPDF) při každém stažení znovu ze snímku faktury.

**Číselná řada bez děr:** sekvence databáze by při rollbacku nechala díru. Číslo se proto zvýší v řádku řady se
`Version` (concurrency token) ve stejné transakci jako faktura; dvě kopie Webu naráz → druhá dostane
`ConcurrencyConflictException`, celá transakce se vrátí a consumer zprávu zopakuje s dalším číslem.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Invoicing/InvoiceIssuedV1.cs` | událost „faktura vystavená“ (číslo, objednávka, částka) |
| `src/MiniShop.Domain/Invoicing/BillableOrder.cs` | kopie objednávky + `InvoiceLine`, zaplaceno / zrušeno, `IsReadyToInvoice` |
| `src/MiniShop.Domain/Invoicing/Invoice.cs` | faktura (`Issue` jen ze zaplacené kopie) |
| `src/MiniShop.Domain/Invoicing/InvoiceNumberSeries.cs` | řada roku, `Next()`, `Version` |
| `src/MiniShop.Application/Invoicing/InvoicingAbstractions.cs` | rozhraní modulu, `InvoiceSeller`, DTO |
| `src/MiniShop.Application/Invoicing/InvoicingHandler.cs` | události → kopie → faktura + `InvoiceIssuedV1` |
| `src/MiniShop.Application/Invoicing/InvoiceService.cs` | PDF (vlastník / admin), číslo k objednávce, přehled |
| `src/MiniShop.Application/Notifications/IInvoiceDocuments.cs`, `InvoiceEmailHandler.cs` | e-mail s fakturou v příloze |
| `src/MiniShop.Infrastructure/Invoicing/*` | `InvoicingDbContext` (schéma `invoicing`), konfigurace, repozitáře, `QuestPdfInvoiceRenderer`, registrace, migrace `InitialInvoicing` |
| `src/MiniShop.Web/Invoicing/InvoiceDocuments.cs` | most Notifications → Invoicing |
| `src/MiniShop.Web/Pages/Orders/Invoice.cshtml(.cs)` | `/Orders/{id}/Invoice` – PDF, cizí = 404 |
| `src/MiniShop.Web/Pages/Admin/Invoices/Index.cshtml(.cs)` | přehled faktur pro admina |
| `tests/MiniShop.Tests/Domain/InvoiceTests.cs`, `Web/InvoicePageTests.cs` | doména, stránky |

## Změněné soubory

- `IEmailSender.cs` – `EmailMessage.Attachments` + `EmailAttachment`; `SmtpEmailSender` přílohy posílá, `LoggingEmailSender` je zapíše do logu.
- `OrderEmailTemplates.Invoice` – šablona e-mailu s fakturou.
- `IntegrationModules.Invoicing`, `DesignTimeDbContextFactories`, `Program.cs` (infrastruktura + migrace),
  `ApplicationServiceCollectionExtensions` (`AddInvoicingModule`, handler v Notifications, `InvoiceDocuments`).
- `Orders/Detail` – „Faktura č. … – stáhnout PDF“; menu admina *Faktury*; `/Privacy` – faktury 10 let i po smazání účtu.
- `MiniShop.Infrastructure.csproj` – balíček `QuestPDF` (licence Community – `QuestPDF.Settings.License`).
- Testy: `OrderServiceTests` (modul Invoicing v celé cestě, 7 testů faktur, `NotifierEvent` zná „invoice“),
  `ModuleBoundariesTests` (Invoicing ↔ Ordering, Payments, Notifications…; Invoicing neposílá e-maily),
  `DatabaseMigrationTests`, `PostgresTestServer`, `TestDatabases`, `AuthorizationMatrixTests`.
- Příručka: modul Invoicing, tok Vystavení faktury, Notifications, E-maily, knihovny (QuestPDF), zálohy, slovníček, historie.

## Rozhodnutí v tomto kroku

- **Samostatný modul, žádný nový kontrakt navíc pro vstup** – stačí `OrderPlacedV2` (má ceny, slevy, adresu)
  a existující události. Ordering o fakturách neví.
- **Neplátce DPH, fiktivní prodávající** (`InvoiceSeller.Default`, přepsat jde `Invoicing:Seller:*`) – výukový projekt.
- **PDF v příloze e-mailu** (volba uživatele) přes `IInvoiceDocuments` ve Webu, ne PDF ve zprávě (velikost, osobní údaje v brokeru).
- **PDF se skládá při stažení** ze snímku faktury (neukládá se) – údaje jsou neměnné, mění se jen vzhled.
- **Faktury zůstávají po smazání účtu** (zákonná povinnost), kopie objednávek se mažou.
- **QuestPDF** místo HTML → PDF (bez prohlížeče v kontejneru), vestavěné písmo Lato (české znaky bez systémových fontů).

## Známá omezení

- **Dobropis** chybí: zrušení zaplacené objednávky nebo vrácení zboží fakturu neopraví (e-shop krok 15B).
- Objednávky zaplacené před nasazením faktury nedostanou (modul nemá jejich kopii; žádná zpětná synchronizace).
- E-mail s fakturou je „at-least-once“ jako ostatní e-maily (pád mezi odesláním a inboxem = e-mail podruhé).
- Rok řady podle času Prahy v okamžiku vystavení; datum úhrady = čas zprávy o zaplacení.

## Jak ověřit

- `dotnet test MiniShop.slnx` – `OrderServiceTests` (Faktura_*), `InvoiceTests`, `InvoicePageTests`,
  `AuthorizationMatrixTests`, `ModuleBoundariesTests`, `DatabaseMigrationTests`.
- Lokálně (AppHost + Mailpit): objednávka kartou → *Zaplatit* → za pár sekund v Mailpitu e-mail „Faktura …“ s PDF,
  na detailu objednávky odkaz *stáhnout PDF*; dobírka → faktura až po *Doručeno* v Expedici.
- V produkci: totéž; `/Admin/Invoices`; `/health` hlídá `outbox-invoicing`.

## Vyzkoušejte sami

1. Objednejte kartou a zaplaťte. Na detailu objednávky se po chvíli objeví *Faktura č. 2026000001 – stáhnout PDF*.
2. Druhá objednávka dostane `…000002` – i když ji zaplatíte dřív, než první dorazí (čísla podle pořadí vystavení).
3. Dobírka: faktura vznikne až po *Doručeno* v Expedici.
4. Nechte platbu kartou vypršet (nebo ji zamítněte) – faktura nevznikne.
5. Jako jiný zákazník zkuste `/Orders/<cizí číslo>/Invoice` → 404.
