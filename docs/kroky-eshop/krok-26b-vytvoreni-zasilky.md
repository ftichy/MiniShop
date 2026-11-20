# E-shop krok 26B: Vytvoření zásilky u dopravce

Plán: [Doprava-roadmap](../Doprava-roadmap.md). Druhý krok napojení na dopravce: skladník už číslo zásilky neopisuje –
klikne „Předat dopravci“ a číslo přidělí (simulovaný) dopravce. Doručení zatím zůstává ruční (krok 26C).

## Jak to funguje

```
Expedice „Předat dopravci“ → Order.RequestShipment → OrderShipmentRequested
  → překladač: CreateShipmentV1 (příkaz)            ── fronta minishop.shipping
  → CreateShipmentHandler → CarrierApiClient → POST /v1/shipments (Idempotency-Key = CommandId)
       201/200 → Shipment(Created) + ShipmentCreatedV1 → Ordering: ConfirmShipment → OrderShipped → OrderShippedV1 (jako dřív)
       422     → Shipment(Failed)  + ShipmentFailedV1  → Ordering: FailShipment → hláška + „Zkusit znovu“
       503/…   → CarrierUnavailableException → consumer zopakuje (5×) → dead-letter → Správa → Zprávy
```

- **Idempotency-Key** chrání dopravce před druhou zásilkou, když spadneme mezi jeho odpovědí a naším zápisem
  (inbox chrání jen naši stranu). Vyzkoušeno zlobením `LostResponseRate`.
- **Překladová vrstva:** `CarrierApiClient` je jediné místo se JSONem a kódy dopravce; ven jdou `CarrierShipmentAccepted`
  / `CarrierShipmentRejected` a české hlášky.
- **Po předání nejde zrušit** (`Order.Cancel` i tlačítko na detailu) ani ručně odeslat – zásilka už může u dopravce
  existovat. Odmítnutí vrátí objednávku do „K odeslání“; ruční odeslání jiným dopravcem zůstává jako záloha.
- Simulátor dostal zlobení `RejectRate` (422 `address_not_serviceable`) – jediná cesta, jak trvalou chybu vyvolat
  (PSČ i délky polí hlídá už naše doména).

Přirovnání: inbox je váš sešit „co jsem už vyřídil“, Idempotency-Key číslo, které řeknete na poště – když přijdete
s tímtéž balíkem podruhé, úřednice najde ten první a nepošle ho znovu.

## Nové soubory

- Kontrakty `src/MiniShop.Contracts/Shipping/` – `CreateShipmentV1`, `ShipmentCreatedV1`, `ShipmentFailedV1`.
- Doména `Domain/Shipping/Shipment.cs` (+ `ShipmentStatus`, události `ShipmentCreated`, `ShipmentFailed`),
  `Domain/Orders/Events/OrderShipmentRequested.cs`.
- Aplikace `Application/Shipping/` – `ICarrierGateway` (+ výsledky, `CarrierUnavailableException`), `IShipmentRepository`,
  `IShippingUnitOfWork`, `CreateShipmentHandler`; `Application/Ordering/ShipmentResultHandler.cs`.
- Infrastruktura `Infrastructure/Shipping/` – `ShippingDbContext` (schéma `shipping`, outbox, inbox), konfigurace,
  repozitář, `CarrierApiClient`, `ShippingInfrastructure` + `ShippingDatabaseInitializer`, migrace `InitialShipping`;
  migrace Orderingu `CarrierShipmentRequest` (sloupce `ShipmentRequestedAtUtc`, `ShipmentError`).
- Testy `OrderServiceTests.Shipping.cs` (6 scénářů proti simulátoru v paměti), příručka `architektura/moduly/shipping.md`.

## Změněné soubory

- `Order` (`RequestShipment`, `ConfirmShipment`, `FailShipment`, `Ship` odmítne během předávání, `Cancel` po předání),
  `OrderDto` (`AwaitingShipment`, `ShipmentError`), `IOrderService`/`OrderService.RequestShipmentAsync`, překladač,
  `OrderingDomainEvents` + reader, konfigurace `Order`, `IntegrationModules.Shipping`.
- Web: `AddShippingModule` (handlery + HTTP klient `https+http://carrier-sim` se `ServiceApiKeyHandler`), `Program.cs`,
  `ServiceApiKey.CarrierConfigurationKey`, `appsettings.Development.json`, stránka Expedice (tlačítko, stav, hláška,
  záložní ruční odeslání, automatické obnovení), `OrderStatusLabels.CanCancel(OrderDto)`, `/Privacy` (dopravce).
- Simulátor: `Chaos:RejectRate`. Compose (Web: adresa dopravce + `CARRIER_API_KEY`, `CARRIER_CHAOS_REJECT_RATE`), AppHost.
- Generátor provozu (`Desk.cs` – předat dopravci a počkat na „Odeslaná“), E2E (`ShopFlow.ShipAndDeliverAsync`, `ExpediceTests`).
- Testy: `PostgresTestServer`, `TestDatabases`, `DatabaseMigrationTests`, `ModuleBoundariesTests`, `IntegrationContractTests`,
  `AuthorizationMatrixTests`, `StaffAreaTests`, `FulfilmentPageTests`, `MiniShopWebFactory`.
- Příručka: moduly, tok Expedice, dopravce, kontejnery, observabilita (`minishop.shipment.result`), generátor; roadmapa; `CLAUDE.md`.

## Rozhodnutí

- **Modul ve Webu, ne služba** (vyhodnocení mikroservis M3). Vlastní schéma, outbox, inbox a fronta – kdyby bylo
  potřeba, jde vyjmout stejně jako Reporting.
- **Příkaz, ne událost** – předat zásilku má právě jeden vykonavatel a Ordering čeká na odpověď.
- **Nová zásilka na každý pokus** (nový příkaz po odmítnutí) – unikátní `CommandId`, vytvořená nejvýš jedna na objednávku.
- **Shipping si adresu neukládá** – zná ji objednávka i dopravce; méně kopií osobních údajů.
- **401 jako dočasná chyba** – je to chyba nastavení; po opravě klíče jde zprávu vrátit z dead-letter.

## Známá omezení

- Zásilku nejde stornovat (simulátor to neumí) – proto po předání nejde zrušit objednávku (krok 26D).
- Dlouhý výpadek dopravce: po 5 pokusech je příkaz v dead-letter a objednávka „Předává se…“, dokud ho někdo nevrátí
  (*Správa → Zprávy*), nebo neodešle jinak. Alarm `MiniShopDeadLetter` se ozve.
- Doručení pořád ručně – webhooky dopravce až v 26C.

## Jak ověřit

1. `deploy.sh` ani secrets se nemění (klíč `CARRIER_API_KEY` už existuje z 26A).
2. Po nasazení: objednávka kurýrem na dobírku → *Expedice* → Začít balit → **Předat dopravci** → za pár sekund
   „Na cestě“ s „Simulovaný dopravce CS…“; zákazník dostane e-mail s číslem zásilky. Pak ručně Doručeno.
3. `docker compose logs carrier-sim` – „Zásilka CS… pro <číslo objednávky> vytvořena“, pak stavy po 30 s.
4. Zrušení na detailu objednávky během „Předává se dopravci…“ není nabídnuté.

## Vyzkoušejte sami

- Lokálně v `deploy/.env` `CARRIER_CHAOS_LOST_RESPONSE_RATE=1` → předání projde napodruhé, u dopravce je jedna zásilka
  (`Idempotent-Replayed` v logu Webu). `CARRIER_CHAOS_REJECT_RATE=1` → hláška „Dopravce na tuto adresu nedoručuje.“
- V Grafaně trasa předání: `minishop.order.id` → span `minishop.shipping` s HTTP voláním `carrier-sim`.
