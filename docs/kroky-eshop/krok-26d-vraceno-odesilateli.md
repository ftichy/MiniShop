# E-shop krok 26D: Vráceno odesílateli

Plán: [Doprava-roadmap](../Doprava-roadmap.md). Poslední krok napojení na dopravce: kurýr zákazníka dvakrát nezastihne
a dopravce zásilku vrátí. E-shop musí vrátit zboží na sklad, u karty peníze a vystavit dobropis. Tentokrát **bez ságy**
(choreografie): o vrácení se nerozhoduje, je to fakt, takže Ordering ho jen oznámí a každý modul si udělá svoje.

## Jak to funguje

```
carrier-sim (Chaos:UndeliverableRate – kostka při vzniku zásilky):
   převzato → na cestě → doručuje se → nezastižen → doručuje se → nezastižen → vrací se → vráceno odesílateli
   webhooky (26C) → Shipment.ApplyTracking: DeliveryFailed / ReturningToSender / ReturnedToSender
        ReturnedToSender → doménová ShipmentReturned → ShipmentReturnedV1 (v téže transakci)
Ordering: ShipmentResultHandler → order.ReturnToSender() (jen ze Shipped, přes ChangeStatus)
        → OrderReturnedToSender → sága Shipped → ReturnedToSender (nic neposílá)
                                → překladač → OrderReturnedToSenderV1 (UDÁLOST, fanout; položky, RefundedByCard)
   ├─ Inventory  RestockReturnedGoodsHandler – vydané rezervace zpět na sklad (bez odpovědi)
   ├─ Payments   PaymentCommandHandlers – kartou: brána RefundAsync → PaymentRefundedV1 (e-mail „vracíme“, tržby)
   ├─ Invoicing  InvoicingHandler – MarkVoided(ReturnedToSender): faktura je → dobropis, dobírka → nefakturovat
   ├─ Notifications  e-mail „Zásilku … se nepodařilo doručit“ (karta: vracíme na kartu, dobírka: neplatíte)
   └─ Reporting  SalesOrderStatus.ReturnedToSender – mimo tržbu (i po přestavění ze snímku)
```

**Orchestrace vs. choreografie na stejném problému.** Vratku od zákazníka (krok 12) řídí sága: je v ní krok člověka
(Přijato/Zamítnout) a čeká se na odpovědi. Vrácení dopravcem nikdo neschvaluje, proto stačí událost – stejně jako
`OrderShippedV1` v kroku 6. Ordering neví, kdo poslouchá; nový odběratel = jen nový handler.

**Idempotence každého odběratele zvlášť** (inbox chrání před stejnou zprávou, vlastní stav i před „stejným faktem jinou
cestou“): sklad bere jen ještě nevrácené rezervace, `Payment.Refund` vrátí false u vrácené platby (a brána má
idempotency key podle platby), Invoicing vystaví k objednávce nejvýš jeden celý dobropis, Reporting posouvá stav jen dopředu.

## Nové soubory

- `src/MiniShop.Contracts/Shipping/ShipmentReturnedV1.cs`, `src/MiniShop.Contracts/Orders/OrderReturnedToSenderV1.cs`
- `src/MiniShop.Domain/Orders/Events/OrderReturnedToSender.cs`
- `tests/MiniShop.Tests/Application/OrderServiceTests.ReturnedToSender.cs` – 5 testů celé cesty

## Změněné soubory

- Simulátor: `CarrierState.cs` (`Undeliverable`, `UndeliverableTimeline`, stavy `delivery_failed`/`returning`/`returned_to_sender`,
  úklid i vrácených), `ShipmentsApi.cs` (kostka při vzniku), `WebhookDispatcher.cs` (časová osa zásilky), `CarrierOptions.cs`
  (`Chaos:UndeliverableRate`), `appsettings.json`.
- Shipping: `Shipment.cs` (nové stavy na konci enumu, `IsFinished`, `ShipmentReturned`), `ShipmentEvents.cs`, `ShippingDbContext.cs`,
  `CarrierApiClient.TranslateStatus`.
- Ordering: `OrderStatus.ReturnedToSender` (10), `Order.ReturnToSender`, `OrderSaga.OrderReturnedToSender` + `OrderSagaState.ReturnedToSender`,
  `OrderSagaOrchestrator`, `PublishOrderIntegrationEventsHandler`, `ShipmentResultHandler`, `OrderingDomainEvents`, `OrderingDomainEventReader`.
- Odběratelé: `RestockReturnedGoodsHandler` (Inventory), `PaymentCommandHandlers` (Payments), `InvoicingHandler` + `CreditNoteReason.ReturnedToSender`
  + text v PDF (Invoicing), `OrderNotificationsHandler` + `OrderEmailTemplates.ReturnedToSender` (Notifications), `SalesOrder` +
  `SalesProjectionHandler` (Reporting).
- Registrace: `ApplicationServiceCollectionExtensions` (Web), `Inventory.Api/Program.cs`, `NotificationsModule`, `ReportingModule`, testy.
- Web: `OrderStatusLabels` („Vráceno dopravcem“), `ShipmentStatusLabels` („Nezastižen“, „Vrací se odesílateli“, „Vráceno odesílateli“),
  detail objednávky (upozornění `order-returned-to-sender`), Přehled tržeb („Vrácené dopravcem“).
- Generátor provozu: čeká na „Doručená“ **nebo** „Vráceno dopravcem“ (trpělivost 8 min).
- `deploy/compose.yaml` + `deploy/production.env` (`CARRIER_CHAOS_UNDELIVERABLE_RATE=0.1`).
- Příručka: `toky/expedice.md` (diagram choreografie), `architektura/dopravce.md`, `moduly/shipping.md`, `moduly/index.md`,
  `eda/observabilita.md`, `nasazeni/generator-provozu.md`, `historie.md`; `Doprava-roadmap.md`.

## Rozhodnutí

- **Choreografie, ne sága** – žádný krok člověka ani rozhodování. Cena: nikdo „nedohlíží“, že všichni doběhli; selhání
  (refund ve Stripu) skončí v dead-letter frontě modulu a hlídá ho alarm `MiniShopDeadLetter`.
- **Nový stav `ReturnedToSender`**, ne `Returned` – ten znamená „zákazník vrátil po doručení“ a navazují na něj e-maily,
  hodnocení a vratky po částech. Objednávka nebyla doručena, takže ji nejde hodnotit ani vracet.
- **Nezastižení zatím nikomu nic neposílá** – je vidět jen v průběhu zásilky.
- **Produkce 10 %** nedoručitelných zásilek, i pro ruční zkoušky (kartou = skutečný refund ve Stripu v testovacím režimu).
- Kontrakt nese `TotalPrice` a `RefundedByCard` (stejně jako `ReturnCompletedV1`) – Notifications nemusí nic dohledávat.
- Žádná migrace: stavy se ukládají jako text, nové hodnoty enumů jsou na konci.

## Známá omezení

- Zásilka, kterou dopravce nezná (`UnknownAtCarrier`), se vrácením nevyřeší – dál ji řeší člověk v Expedici.
- Dobírka po vrácení: nic se neplatilo ani nefakturovalo; poštovné za vrácení e-shop neúčtuje.
- Pozdní návrat po ručním „Doručeno“ nic nezmění (zásilka v modulu Shipping je vrácená, objednávka doručená).

## Jak ověřit

- `dotnet build MiniShop.slnx`, `dotnet test MiniShop.slnx` (nové `VracenoDopravcem_*` a `ZlobeniNedorucitelna_*`).
- V produkci po nasazení: ~každá desátá předaná zásilka skončí „Vráceno dopravcem“ (~4 min po předání).

## Vyzkoušejte sami

1. Lokálně nastavte simulátoru `Carrier:Chaos:UndeliverableRate` = `1` (AppHost / `appsettings.json` simulátoru).
2. Objednejte kurýrem kartou, zaplaťte, v Expedici zabalte a „Předat dopravci“.
3. Na detailu objednávky sledujte průběh zásilky: Nezastižen → Doručuje se → Nezastižen → Vrací se → Vráceno odesílateli.
4. Objednávka je „Vráceno dopravcem“, zboží zpět ve skladu, v Mailpitu e-mail „nepodařilo doručit“ a „vracíme“,
   ve Fakturách dobropis, v Přehledu tržeb stav „Vrácené dopravcem“ bez tržby.
5. V Grafaně / Aspire: `{ span.minishop.stock.result = "returned_to_sender" }`.
