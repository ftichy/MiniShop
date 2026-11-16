# E-shop krok 22C: Vratka po částech – Ordering a obrazovky

Poslední ze tří částí issue #119. Sklad a Payments (22A) i dobropisy, e-maily, tržby a hodnocení (22B) už zprávy V2 umí –
teď je Ordering začne posílat a funkce se zapne:

| Část | Co | Kdo posílá nové zprávy |
| --- | --- | --- |
| 22A | sklad a Payments umí vratku po částech | nikdo |
| 22B | dobropisy, e-maily, tržby, hodnocení | nikdo |
| **22C (tento)** | Ordering – výběr kusů, sága pro každou vratku, obrazovky | **Ordering** |

Přirovnání: na poště už všechny přepážky umí nový tiskopis (22A, 22B), teď ho dostanou zákazníci.

## Jak to funguje

1. **Zákazník** na detailu doručené objednávky rozbalí *Vrátit zboží* a u každé položky vybere počet kusů (výchozí = vše,
   co jde vrátit). Formulář posílá `quantities[ID produktu]`.
2. **`Order.RequestReturn(důvod, kusy, čas, okno)`** rozhodne:
   - všechno jako první vratka → **vratka celé objednávky** (dnešní cesta: `ReturnRequested` → `Returned`, V1 zprávy,
     vrací se i doprava), vrací číslo 0;
   - jinak **vratka po částech**: `OrderReturn` s číslem 1, 2, … a řádky `OrderReturnLine` (cena z objednávky, podíl slevy
     z kuponu poměrně na kusy – poslední kusy dostanou zbytek), událost `OrderPartialReturnRequested`. Objednávka zůstává
     doručená.
3. **Sága vratky** (`ReturnSaga`, klíč objednávka + `ReturnNumber`) běží stejně jako u celé objednávky, jen posílá V2:
   převzetí adminem → `RestockReturnedGoodsV2` (jen vracené kusy) → `ReturnedGoodsRestockedV2` → kartou `RefundPaymentV2`
   (částka vratky) → `PaymentRefundedV2` → `Order.CompletePartialReturn` → `OrderPartialReturnCompleted`. Dobírka po
   naskladnění rovnou konec. Zamítnutí a vypršení → `Order.RejectPartialReturn` → `OrderPartialReturnRejected`.
4. **Překladač** (`PublishOrderIntegrationEventsHandler`) pošle `ReturnRequestedV2`, `ReturnRejectedV2`, `ReturnCompletedV2`
   (položky, částka, `FullyReturnedProductIds` – produkty, ze kterých zákazníkovi po dokončených vratkách nezbyl kus).
5. **Admin** na stránce Vratky vidí řádek na každou vratku (číslo, vracené kusy, částka); *Přijato* a *Zamítnout* posílají
   i číslo vratky.
6. **Přestavění přehledu tržeb** – Ordering pošle vedle `OrderSnapshotV1` i `OrderPartialReturnsSnapshotV1` (jen u objednávek
   s vyřízenou vratkou po částech).

## Nové soubory

- `src/MiniShop.Domain/Orders/OrderReturn.cs` (`OrderReturn`, `OrderReturnLine`, `OrderReturnStatus`)
- Doménové události `OrderPartialReturnRequested`, `OrderPartialReturnRejected`, `OrderPartialReturnCompleted`
- Kontrakt `OrderPartialReturnsSnapshotV1` + `PartialReturnSnapshotV1`
- Migrace `PartialReturns` (Ordering): tabulky `OrderReturns`, `OrderReturnLines`; `ReturnSagas.ReturnNumber` (výchozí 0)
  a primární klíč objednávka + číslo vratky
- Testy `tests/MiniShop.Tests/Domain/OrderReturnTests.cs`, E2E `tests/MiniShop.E2E/VratkaTests.cs`

## Změněné soubory

`Order` (`Returns`, `OpenReturn`, `ReturnableQuantity`, `CanRequestPartialReturn`, `RequestReturn` s kusy,
`CompletePartialReturn`, `RejectPartialReturn`, `GetReturn`, `FullyReturnedProductIds`; `CanRequestReturn` a `IsActive`),
`ReturnSaga` (`ReturnNumber`, `IsPartial`), `ReturnSagaOrchestrator` (V2, číslo vratky ve všech krocích), `IReturnSagaRepository`
(`GetAsync` s číslem, `GetExpiredAsync` → `ReturnSagaKey`), `PublishOrderIntegrationEventsHandler`, `RebuildSalesReportHandler`,
`SalesProjectionHandler` (snímek vratek), `OrderService`/`IOrderService` (`RequestReturnAsync` s kusy, `GetReturnQueueAsync`
→ `OrderDetailDto`), `IOrderRepository.GetReturnQueueAsync`, DTO (`OrderReturnDto`, `ReturnableItemDto`), `OrderingDomainEvents`
+ `OrderingDomainEventReader` (číslo vratky v payloadu), EF konfigurace, `OrderSagaTimeoutWorker`, stránky `Orders/Detail`
a `Admin/Returns`, `site.css` (`.return-qty`), registrace, příručka (tok Vrácení zboží, moduly Ordering a Reporting,
observabilita, CI – E2E scénáře, historie), CLAUDE.md.

## Rozhodnutí

- **Vratka po částech je entita v agregátu `Order`**, ne samostatný agregát: pravidlo „nevrátit víc, než se koupilo“ potřebuje
  položky i všechny vratky najednou a souběh dvou formulářů hlídá `Order.Version`.
- **Jedna rozpracovaná vratka najednou** – jednodušší pro zákazníka i admina, žádný souběh dvou vratek stejných kusů.
- **Celá objednávka jen jako první vratka** (cesta V1, vrací se doprava); po vratce po částech už jen další vratky po
  částech a **doprava se nevrací**, ani když zákazník postupně vrátí všechno. Po vratce celé objednávky (i zamítnuté) už
  žádná další – jako dřív.
- **Zamítnutá vratka po částech** další vratce nebrání – kusy se do vrácených nepočítají.
- **Klíč ságy objednávka + číslo vratky**, celá objednávka = 0 (i všechny stávající ságy). Migrace přidá sloupec s výchozí
  hodnotou a změní primární klíč – starší verze kódu ságy bez čísla zapisuje (= 0) a hledá podle objednávky, takže při
  postupném nasazení funguje dál.
- **Číslo vratky v payloadu outboxu** (`OrderPayload.ReturnNumber`, jen u událostí vratky – starší řádky ho nemají a JSON
  ostatních událostí se nemění).
- **Snímek vratek jako samostatná zpráva** `OrderPartialReturnsSnapshotV1` místo nové verze `OrderSnapshotV1` – snímek
  objednávky zůstává beze změny a Reporting vratky přidá v libovolném pořadí (vratka se jen přidává).
- **Odpověď V2 bez ságy** (ruční zásah, testy 22A) Ordering ignoruje – stejně jako `PaymentRefundedV1`.
- **Formulář bez počtů kusů** (starší stránka v prohlížeči) = vratka celé objednávky jako dřív.

## Známá omezení

- Snímek nákupů pro Reviews (`OrderSnapshotV2`, jen při prvním naplnění modulu) vratky po částech nenese – hodnocení produktu
  vráceného celého by po takovém naplnění šlo. Reviews se plní jen nad prázdnou DB, v produkci už naplněné jsou.
- Doprava se u vratky po částech nevrací nikdy – ani u poslední vratky, která vrací všechno zbylé (rozhodnutí issue #119).
- Částečný refund přes skutečný Stripe ověřuje člověk v produkci (testy používají podvržený `HttpClient`).

## Jak ověřit

- `dotnet test` – `OrderReturnTests` (pravidla, sleva, jedna rozpracovaná, celá objednávka jen jako první),
  `OrderServiceTests.VratkaPoCastech_Kartou_CelaCesta` (sága, sklad, Payments, dobropis, e-mail, přestavění tržeb),
  `…_Dobirka_BezVraceniPenez`, `…_ZamitnutiAVyprseni`, `…_VsechnoNaPoprve_JeCelaObjednavka`,
  `IntegrationContractTests.OrderPartialReturnsSnapshotV1_*`.
- E2E v CI: scénář *Vratka po částech* (2 kusy na dobírku, vrátit 1, admin převezme).
- Produkce (po ruční záloze): objednávka kartou se 2 kusy → doručit → vrátit 1 kus → Vratky → Přijato → ve Stripu
  (testovací režim) částečný refund, na detailu vratka vyřízená a dobropis jen za 1 kus, objednávka dál „Doručená“.

## Vyzkoušejte sami

1. V `OrderReturnTests.Cast_PosledniKusyDostanouZbytekSlevy_SoucetSedi` změňte slevu myši na 10,01 Kč – součet slev vratek
   pořád sedí přesně (poslední vratka dostane zbytek).
2. Na detailu doručené objednávky pošlete vratku a hned druhou – druhá skončí hláškou „Předchozí vratka se ještě vyřizuje“.
