# E-shop krok 11: Přehled tržeb pro admina (CQRS)

Admin má novou stránku **Přehled** (`/Admin/Sales`): tržby dnes a za 14 dní po dnech, nejprodávanější produkty
za 30 dní, počty objednávek podle stavu a vrácené peníze. Data nečte z objednávek – nový modul **Reporting** si je
skládá do vlastní tabulky (**čtecí model**, CQRS) z integračních událostí, které už existují. Čtecí model jde
kdykoli zahodit a postavit znovu ze snímků objednávek. Volitelný námět z [docs/Eshop-roadmap.md](../Eshop-roadmap.md),
navazuje na [krok 10](krok-10-opusteny-kosik.md).

## Jak to funguje

Přirovnání: účetní nechodí počítat zboží do skladu, vede si knihu podle dokladů. Když o ni přijde, nechá si poslat
kopie všech dokladů a sepíše ji znovu.

```
 Ordering ─ OrderPlacedV2, OrderConfirmedV1, OrderRejectedV1, OrderCancelledV1, OrderCompletedV1 ─┐
 Payments ─ PaymentRefundedV1 ──────────────────────────────────────────────────────────────────┤
                                                    RabbitMQ: fronta minishop.reporting (inbox)   ▼
                                   SalesProjectionHandler ─► reporting.db: SalesOrders (stav jen dopředu)
 admin ─► /Admin/Sales ─► SalesReportService (jen čtecí model) ─► tržby, top 10, stavy, „data k …“

 přestavění:  Přestavět / prázdná DB po startu ─► smazat SalesOrders + RebuildSalesReportV1 (jedna transakce)
              ─► Ordering: OrderSnapshotV1 pro každou objednávku ─► projekce (stejný upsert)
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **CQRS** | zápis a čtení mají oddělené modely | Ordering (agregát `Order`) × Reporting (`SalesOrder`) |
| **Projekce / čtecí model** | data poskládaná z událostí přesně pro dotazy | `SalesProjectionHandler` → `SalesOrders` |
| **Nový odběratel bez změny odesílatele** | přidat reakci na existující událost = nový handler, nic víc | Ordering ani Payments se nezměnily (kromě snímků) |
| **Zprávy mimo pořadí** | upsert + stav jen dopředu = výsledek nezávisí na pořadí | `SalesOrder.Create`, `ApplyStatus` |
| **Přestavění projekce** | čtecí model je odvozený, jde ho zahodit a postavit znovu | `RebuildSalesReportV1` → `OrderSnapshotV1` |
| **Eventual consistency** | čtení je o chvilku pozadu | „data k …“ na stránce |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Reporting/RebuildSalesReportV1.cs`, `Orders/OrderSnapshotV1.cs` | příkaz na přestavění, snímek objednávky |
| `src/MiniShop.Domain/Reporting/SalesOrder.cs` | řádek čtecího modelu (+ `SalesOrderStatus`, `SalesOrderLine`) |
| `src/MiniShop.Application/Reporting/*` | `SalesProjectionHandler`, `SalesReportService` (+ DTO), rozhraní modulu |
| `src/MiniShop.Application/Ordering/IntegrationEvents/RebuildSalesReportHandler.cs` | Ordering pošle snímky všech objednávek |
| `src/MiniShop.Infrastructure/Reporting/*` | `ReportingDbContext` (inbox + outbox), repozitář, pošťák, migrace `InitialReporting` |
| `src/MiniShop.Web/Pages/Admin/Sales/Index.cshtml(.cs)`, `BackgroundServices/SalesReportOnStartupWorker.cs` | stránka Přehled, přestavění po startu |
| `docs/prirucka/eda/cqrs.md`, `architektura/moduly/reporting.md`, tento souhrn | dokumentace |
| `tests/…/Domain/SalesOrderTests.cs`, `Web/SalesReportPageTests.cs` | stav jen dopředu, stránka + přestavění |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Application/IntegrationModules.cs` | `Reporting` (fronta `minishop.reporting`) |
| `Web/Program.cs`, `Extensions/…`, `appsettings.json`, `Dockerfile` | `reporting.db`, `AddReportingModule`, worker, handler snímků v Orderingu |
| `Web/Pages/Shared/_Layout.cshtml` | menu **Přehled** (jen admin) |
| `deploy/backup.py` | `reporting.db` v noční záloze |
| příručka (`moduly/index`, `eda/index`, `slovnicek`, `historie`) | dokumentace |
| `tests/…/OrderServiceTests.cs`, `AuthorizationMatrixTests.cs`, `ModuleBoundariesTests.cs`, `MiniShopWebFactory.cs` | modul v testech, oprávnění, hranice |

## Rozhodnutí v tomto kroku

- **Řádek na objednávku, součty při čtení** – ne předpočítané denní součty. Zrušení nebo pozdní zpráva jen změní
  stav jednoho řádku; součet za den by se musel „odečítat“ a hlídat pořadí. Dat je málo (30 dní).
- **Stav jen dopředu** místo čekání na správné pořadí (jako Notifications) – projekce nic neposílá, nemusí čekat.
- **Nový kontrakt `OrderSnapshotV1`** pro přestavění – znovu poslané `OrderPlacedV2` by rozeslalo e-maily.
- **Bez osobních údajů** – přehled neukládá jméno ani e-mail, smazání účtu ho nezajímá.
- **Přestavění automaticky jen nad prázdnou DB** (první nasazení), jinak tlačítkem.
- **Tržba včetně čekajících objednávek** (ještě nepotvrzených) – přehled ukazuje, co se právě děje; zamítnutí je pak odečte.

## Známá omezení

- Přestavění neobnoví vrácené platby (zná je jen Payments) – karta „vráceno“ je pak 0. Do tržby se nepočítají.
- Snímky všech objednávek jdou v jedné transakci – u velkého obchodu by se posílaly po dávkách.
- Během přestavění je přehled pár sekund neúplný.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu migrace `…_InitialReporting` a „Přehled tržeb je prázdný – žádám Ordering o snímky“; v RabbitMQ fronta `minishop.reporting`.
2. Přihlas se jako admin → **Přehled**: staré objednávky jsou v přehledu (ze snímků), „data k …“.
3. Udělej objednávku jako zákazník → za pár sekund se zvýší dnešní tržba a počet.
4. Zruš ji → tržba klesne, v počtech „Zrušené“ +1.
5. **Přestavět přehled** → stránka chvilku ukazuje méně, po obnovení stejná čísla. Žádné nové e-maily v Mailpitu.

### Produkce

Nic nového k nastavení – `reporting.db` vznikne sama, po startu se přehled postaví ze snímků všech objednávek.

## Vyzkoušejte sami

- Proč projekce řádek *založí*, když přijde potvrzení dřív než vznik, místo aby vyhodila výjimku jako Notifications?
- Přidej do přehledu „průměrná hodnota objednávky“ – co musíš změnit v Orderingu? (Nic.)
- Co by se stalo, kdyby `RebuildSalesReportHandler` posílal `OrderPlacedV2` místo snímku?
- Změň, co přehled počítá (např. tržba až od potvrzení), a přestav ho – proč u CQRS nepotřebuješ migraci dat?
