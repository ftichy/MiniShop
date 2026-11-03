# E-shop krok 12: Vrácení zboží

Zákazník může doručenou objednávku vrátit – do 14 dní, celou a jednou. Proces trvá dny a jde přes tři účastníky,
proto ho řídí **druhá sága** (`ReturnSaga` + `ReturnSagaOrchestrator`) vedle objednávkové: zákazník pošle zboží →
admin ho převezme → sklad naskladní → Payments vrátí peníze. Uprostřed je **krok člověka** (převzetí zásilky).
Volitelný námět z [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 11](krok-11-prehled-trzeb.md).

## Jak to funguje

Přirovnání: reklamace v obchodě – lístek na pultu (žádost), zboží na sklad, a peníze až po převzetí. Rozbité zboží
zamítnou, a kdo nepřinese nic do termínu, ten má smůlu.

```
 zákazník ─ Vrátit zboží (detail) ─► Order ReturnRequested ─► OrderReturnRequested
                                       ├─► ReturnSagaOrchestrator: ReturnSaga AwaitingGoods (termín 14 dní)
                                       └─► překladač: ReturnRequestedV1 ─► Notifications „pošlete nám zboží do …“
 admin ─ Vratky: Přijato ─► saga Restocking ─► RestockReturnedGoodsV1 ─► sklad: vydané rezervace → AddStock
                           ◄─ ReturnedGoodsRestockedV1 ─┘
       kartou:  saga Refunding ─► RefundPaymentV1 ─► Payments ─► PaymentRefundedV1 ─► saga Completed
       dobírka: saga Completed (peníze převodem ručně)
       Completed ─► Order Returned ─► ReturnCompletedV1 ─► Notifications, Reporting (z tržby pryč)
 admin ─ Zamítnout / timeout 14 dní ─► Rejected / Expired ─► Order zpět Completed ─► ReturnRejectedV1
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Druhá sága** | samostatný dlouhý proces s vlastním deníkem a dirigentem | `ReturnSaga` (tabulka `ReturnSagas`), `ReturnSagaOrchestrator` |
| **Krok člověka v sáze** | sága čeká na rozhodnutí člověka stejně jako na zprávu, jen s delším termínem | stránka Vratky → `AcceptGoodsAsync` / `RejectAsync` |
| **Opětovné použití kontraktu** | nový proces použije existující příkaz – účastník se nemění | `RefundPaymentV1` / `PaymentRefundedV1` (Payments beze změny) |
| **Kompenzace úspěšného nákupu** | nákup se povedl, ale vracíme ho | `RestockReturnedGoodsV1` (sklad), `RefundPaymentV1` |
| **Dlouhý timeout** | termín ve dnech, hlídá stejný worker | `OrderSagaTimeoutWorker` → `ReturnSagaOrchestrator.TimeOutAsync` |

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Contracts/Orders/ReturnRequestedV1.cs`, `ReturnRejectedV1.cs`, `ReturnCompletedV1.cs` | fakta o vratce (Notifications, Reporting) |
| `src/MiniShop.Contracts/Inventory/RestockReturnedGoodsV1.cs`, `ReturnedGoodsRestockedV1.cs` | příkaz a odpověď skladu |
| `src/MiniShop.Domain/Orders/Saga/ReturnSaga.cs`, `Orders/Events/OrderReturn*.cs` | sága vratky, doménové události |
| `src/MiniShop.Application/Ordering/Saga/ReturnSagaOrchestrator.cs`, `IReturnSagaRepository.cs` | orchestrátor (+ `ReturnSettings`), repozitář |
| `src/MiniShop.Application/Inventory/RestockReturnedGoodsHandler.cs` | naskladnění podle vydaných rezervací |
| `src/MiniShop.Infrastructure/Ordering/ReturnSagaRepository.cs`, `Configurations/ReturnSagaConfiguration.cs` | EF, migrace `Returns` (ordering.db), `ReturnedReservations` (inventory.db) |
| `src/MiniShop.Web/Pages/Admin/Returns/Index.cshtml(.cs)` | stránka Vratky (jen admin) |
| `docs/prirucka/toky/vraceni-zbozi.md`, tento souhrn | dokumentace |
| `tests/…/Domain/ReturnSagaTests.cs` | stav × zpráva, pravidla vrácení na objednávce |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/Order.cs`, `OrderStatus.cs` | `ReturnRequested = 8`, `Returned = 9`; `RequestReturn`, `RejectReturn`, `CompleteReturn`, `CanRequestReturn`, `DeliveredAtUtc`, `ReturnReason`, `ReturnRejectionReason`; vratka = rozpracovaná (`IsActive`) |
| `Domain/Inventory/StockReservation.cs`, `IStockReservationRepository` | `ReturnedAtUtc`, `MarkReturned`, `GetReturnableByOrderIdAsync` |
| `Application/Ordering/OrderService.cs`, `IOrderService`, `OrderContracts.cs` | `RequestReturnAsync`, `GetReturnQueueAsync`, detail s `ReturnableUntilUtc` a důvody |
| `Application/Ordering/IntegrationEvents/PublishOrderIntegrationEventsHandler.cs` | překlad tří událostí vratky |
| `Infrastructure/Ordering/OrderingDomainEvents.cs`, `OrderingDomainEventReader.cs` | nové doménové události v outboxu |
| `Application/Notifications/*` | e-maily vratky; „platbu vracíme“ obecně (zrušení, vratka, pozdní platba) |
| `Domain/Reporting/SalesOrder.cs`, `Application/Reporting/SalesProjectionHandler.cs` | stav `Returned` (z doručené), `ReturnCompletedV1`, snímek `Returned` |
| `Web/Pages/Orders/Detail.cshtml(.cs)`, `Web/Orders/OrderStatusLabels.cs`, `_Layout.cshtml` | tlačítko **Vrátit zboží**, stavy vratky, menu **Vratky** |
| `Web/BackgroundServices/OrderSagaTimeoutWorker.cs` | i timeouty vratek |
| `Web/Extensions/…`, `Inventory.Api/Program.cs`, `appsettings.json` | registrace, sekce `Returns` (`WindowDays`, `SendWindowDays`) |
| příručka (stavové diagramy, `eda/saga`, moduly, toky, historie) | dokumentace |
| `tests/…/OrderServiceTests.cs`, `SalesOrderTests.cs`, `AuthorizationMatrixTests.cs` | celé cesty, Returned v přehledu, oprávnění |

## Rozhodnutí v tomto kroku

- **Celá objednávka**, ne jednotlivé položky – částečná vratka by znamenala poměrnou dopravu, slevy a částečné rezervace.
- **Dobírka: peníze ručně** – Payments je nikdy nepřijal; sága krok vrácení přeskočí a řekne to e-mailem i na stránce.
- **Jedna vratka na objednávku** (klíč `ReturnSagas` = `OrderId`, `CanRequestReturn` hledá v historii) – žádné
  smyčky „zamítnuto → znovu“.
- **Zamítnutí a vypršení = zpět `Completed`**, ne nový stav – objednávka je pořád doručená; důvod je v historii a v
  `ReturnRejectionReason`.
- **Sklad naskladní podle svých vydaných rezervací**, ne podle objednávky (jako uvolnění) – a jen jednou (`ReturnedAtUtc`).
- **`RefundPaymentV1` beze změny** – Payments neví, proč vrací; `PaymentRefundedV1` poslouchá i orchestrátor vratky
  a bez ságy vratky ji ignoruje.
- **Důvod vratky na objednávce** (`ReturnReason`) – události nesou jen objednávku (handlery čtou aktuální stav).

## Známá omezení

- Admin nemůže vratku vyřídit „napůl“ (část zboží v pořádku, část ne) – jen přijmout nebo zamítnout celou.
- Adresa pro vrácení je v textu e-mailu a stránky natvrdo.
- Vrácení peněz u dobírky systém neeviduje (admin si ho hlídá sám).
- Objednávky doručené před e-shop krokem 5 nemají čas doručení – vrátit je nejde.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu migrace `…_Returns` (Web) a `…_ReturnedReservations` (Inventory.Api).
2. Objednej kartou, zaplať → admin **Expedice**: zabalit, odeslat, Doručeno.
3. Zákazník → detail objednávky → **Vrátit zboží** (důvod) → stav „Vrací se“, v Mailpitu „pošlete nám zboží“.
4. Admin → **Vratky** → **Přijato v pořádku** → za pár sekund objednávka „Vrácená“, zboží zpět na skladu, e-maily
   „vracíme“ a „je vrácená“, v **Přehledu** stav Vrácené +1 a tržba o objednávku nižší.
5. Jiná objednávka: **Zamítnout** s důvodem → „Doručená“ s hláškou „Vrácení nebylo přijato“, tlačítko Vrátit zmizí.
6. Dobírka: po převzetí „Vrácená“, e-mail „pošleme převodem“, žádné `RefundPaymentV1` v logu.

### Produkce

Nic nového k nastavení – migrace `ordering.db` a `inventory.db` proběhnou samy při startu.

## Vyzkoušejte sami

- Proč `ReturnSagaOrchestrator` ignoruje `PaymentRefundedV1`, ke které nemá ságu? Odkud taková zpráva může přijít?
- Co se stane, když admin klikne **Přijato** ve stejné vteřině, kdy vratka vyprší? Najdi pojistku.
- Navrhni částečnou vratku (jen některé položky) – co by se muselo změnit ve skladu a v Payments?
- Proč sklad naskladňuje podle rezervací, a ne podle položek objednávky z `RestockReturnedGoodsV1`?
