# E-shop krok 28C: Zásilka za každý balík (směr C, krok C3)

Plán: [Sklady-roadmap](../Sklady-roadmap.md). Rozdělená objednávka (balíky z kroku 28B) už neodchází jako jedna zásilka:
každý sklad předá dopravci **svůj balík**, balík má vlastní číslo zásilky a průběh. Objednávka je **odeslaná s posledním
balíkem** a **doručená, až skončí všechny**. Zákazník dostane e-mail a push za každý balík a na detailu vidí oddíl Balíky.

## Jak to funguje

```
Expedice „Předat dopravci“ u balíku 1 (Praha)          … a u balíku 2 (Brno)
  → Order.RequestParcelShipment(1) → OrderParcelShipmentRequested → CreateShipmentV2 { ParcelNumber 1, Praha, dobírka 300 }
  → Shipping: Shipment (OrderId + ParcelNumber), Idempotency-Key = CommandId → dopravce
  → ShipmentCreatedV1 + ShipmentCreatedV2 { ParcelNumber 1, CS… }      (expand → contract)
  → Ordering: Order.ConfirmParcelShipment(1) → OrderParcelShipped → ParcelShippedV1 → e-mail + push „Balík 1 z 2“
     poslední balík odešel? → OrderShipped → OrderShippedV1 (jednou – sklad vydá rezervace, e-mail o odeslání)

Webhook „doručeno“ (číslo zásilky CS…) → ShipmentDeliveredV1 → Order.MarkParcelDelivered(CS…)
     všechny balíky skončily? → aspoň jeden doručen = OrderCompleted; všechny vrácené = OrderReturnedToSender
```

Teorie:

- **Scatter-gather v jednom agregátu:** jeden pokyn („odeslat objednávku“) se rozpadne na N zásilek a výsledky se sbírají
  v objednávce. Proměnlivý počet účastníků zvládne agregát sám – pravidla „poslední balík odešel“ a „všechny skončily“
  hlídá `Order` v jedné transakci, žádná nová sága není potřeba.
- **Identita bez nových verzí:** doručení i vrácení už nesou číslo zásilky – Ordering podle něj najde balík. Nové verze
  potřebovaly jen zprávy, které číslo balíku jinak nemají (příkaz a odpověď na něj).
- **Expand → contract:** Shipping posílá V1 i V2; Ordering se rozhoduje podle V2 a V1 nechává jen pro starší předání.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Contracts/Shipping/CreateShipmentV2.cs` | příkaz „předat balík“ (číslo balíku, sklad-odesílatel, dobírka balíku) |
| `src/MiniShop.Contracts/Shipping/ShipmentCreatedV2.cs` | `ShipmentCreatedV2` + `ShipmentFailedV2` s číslem balíku |
| `src/MiniShop.Contracts/Orders/ParcelShippedV1.cs` | balík je na cestě (pro e-mail a push) |
| `src/MiniShop.Domain/Orders/Events/OrderParcelEvents.cs` | `OrderParcelShipmentRequested`, `OrderParcelShipped` |
| `src/MiniShop.Infrastructure/{Ordering,Shipping}/Migrations/…_ParcelShipments.cs` | stav balíku (sloupce), `Shipments.ParcelNumber` (výchozí 1) + unikátní index na balík |
| `tests/MiniShop.Tests/Application/OrderServiceTests.Parcels.cs` | dva balíky celou cestou (předání, e-mail, odeslání, dobírka, doručení), předání bez čísla |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Domain/Orders/OrderParcel.cs`, `Order.cs` | stav zásilky balíku; `RequestParcelShipment`, `RequestPendingParcelShipments`, `ConfirmParcelShipment`, `FailParcelShipment`, `MarkParcelDelivered`, `ReturnParcelToSender`; zrušit / ručně odeslat jen bez předaného balíku |
| `src/MiniShop.Domain/Shipping/Shipment.cs`, `Infrastructure/Shipping/…` | `ParcelNumber`, V1 + V2 do outboxu, index na balík |
| `src/MiniShop.Application/Shipping/CreateShipmentHandler.cs`, `ShipmentQueryService.cs` | V2 handler; dotaz na všechny zásilky objednávky (slovník = první balík, ať nespadne na dvou) |
| `src/MiniShop.Application/Ordering/ShipmentResultHandler.cs` | V2 po balících, doručení/vrácení podle čísla zásilky, V1 jen pro starší předání |
| `…/IntegrationEvents/PublishOrderIntegrationEventsHandler.cs`, `OrderingDomainEvents.cs`, `OrderingDomainEventReader.cs` | překlad nových událostí, číslo balíku v payloadu |
| `OrderService.cs`, `IOrderService.cs` | `RequestParcelShipmentAsync`; `RequestShipmentAsync` předá všechny čekající balíky |
| `OrderContracts.cs`, `OrderMapping.cs` | stav balíku v `OrderParcelDto`, hláška dopravce z balíku |
| Notifications: `OrderEmailTemplates`, `OrderNotificationsHandler`, `PushTemplates`, `OrderPushHandler`, `NotificationsModule` | e-mail a push „balík N z M“ (jen u víc balíků) |
| Web: `Admin/Fulfilment`, `Orders/Detail`, `Orders/OrderStatusLabels.cs` | tlačítko u každého balíku, oddíl Balíky s průběhem zásilek, stav balíku česky |
| příručka: `moduly/shipping.md`, `moduly/ordering.md`, `eda/observabilita.md`, `historie.md` | popis, atribut `minishop.shipment.parcel` |

## Rozhodnutí

- **Každá objednávka předává po balících** – i jednobalíková (jeden balík). Jedna cesta místo dvou; starší cesta zůstává jen
  pro odpovědi z doby před nasazením.
- **Dobírka po balících:** každý balík vybere své položky (po slevě), první navíc dopravu – dohromady celá objednávka.
- **`OrderShippedV1` jednou** – s posledním balíkem, s číslem zásilky prvního balíku. Sklad vydává rezervace až tehdy.
- **E-mail/push za balík jen u víc balíků** – u jednoho by byl dvakrát tentýž e-mail.
- **Ruční odeslání a osobní odběr** zůstávají za celou objednávku.

## Známá omezení

- **Vrácený jeden z víc balíků** se zatím jen označí (`ReturnedToSender` u balíku); objednávka se dokončí podle ostatních.
  Peníze, sklad a dobropis za vrácenou část dořeší krok 28D (automatická vratka po částech) – nasadit brzy po 28C.
- Ve frontě Expedice je rozdělená objednávka v „K odeslání“, dokud neodejde poslední balík.

## Jak ověřit

1. `dotnet test MiniShop.slnx` – mimo jiné `Baliky_*`, stávající testy dopravce (`OrderServiceTests.Shipping`).
2. Po nasazení rozdělená objednávka: v Expedici u každého balíku „Předat dopravci“; po prvním zůstane objednávka v „K odeslání“
   a balík ukazuje číslo zásilky; po druhém je odeslaná. Zákazník má e-mail „Balík 1 z 2…“ a na detailu oddíl Balíky.
3. Po doručení obou balíků (asi 2 minuty) je objednávka doručená.

## Vyzkoušejte sami

- Předejte jen balík z Brna a zkuste objednávku zrušit – nejde (balík už jede).
- Filtr skladu Praha: u rozdělené objednávky jde předat jen pražský balík (tlačítko brněnského je zašedlé).
