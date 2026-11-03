# E-shop krok 6: Expedice

Dřív tlačítko „Vyřídit“ přeskočilo celou expedici: z potvrzené objednávky byla rovnou vyřízená. Teď objednávka
prochází stavy **potvrzená → balíme → odeslaná → doručená**. Admin je posouvá na nové stránce **Expedice**
(`/Admin/Fulfilment`). Zákazník vidí dopravce a číslo zásilky na detailu objednávky a dostane e-mail.
Sklad konečně pozná, že zboží odešlo: rezervace se při odeslání změní na **výdej**.
Plán: [docs/Eshop-roadmap.md](../Eshop-roadmap.md), navazuje na [krok 5](krok-05-detail-objednavky.md) (historie stavů).

## Jak to funguje

Přirovnání: rezervace je zboží odložené pod pultem se jménem zákazníka. Při zrušení se vrátí do regálu
(kompenzace z kroku 9). Při odeslání se z „odloženo“ stane „prodáno a předáno“ a vrátit už nejde.
Expedice to oznámí jednou vývěskou (událostí) a každý, koho to zajímá, si ji přečte sám.

```
 Pending → Confirmed → Packing → Shipped → Completed (= doručeno)
              │           │
              └───────────┴──► Cancelled            (po odeslání už zrušit nejde)

 Admin „Odeslat“ ─► Order.Ship(dopravce, číslo) ─► OrderShipped (doménová)
                                                     ├─► sága: Reserved → Shipped (nic neposílá)
                                                     ├─► e-mail „na cestě, číslo zásilky …“
                                                     └─► OrderShippedV1 (integrační, fanout)
                                                            └─► sklad: FulfilReservationHandler → rezervace vydaná
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Orchestrace** | dirigent posílá příkazy a čeká na odpovědi | sága → `ReserveStockV1` → `StockReservedV1` (rezervace může selhat) |
| **Choreografie** | každý reaguje na oznámený fakt sám | `OrderShippedV1` → sklad vydá rezervaci (výdej selhat nemůže) |
| **Optimistická souběžnost** | nezamyká se; při uložení se ověří, že data nikdo mezitím nezměnil | `Order.Version` (concurrency token), `UPDATE … WHERE Version = původní` |
| **Výdej rezervace** | rezervace skončila odesláním, ne vrácením | `StockReservation.Fulfil()` / `FulfilledAtUtc` |

**Souběh odeslat × zrušit:** admin a zákazník načtou objednávku se stejnou `Version`. První uložení projde a verzi
zvýší. Druhé narazí na `WHERE Version = původní`, nezmění žádný řádek a EF vyhodí výjimku, kterou `ModuleDbContext`
převede na `ConcurrencyConflictException`. Výsledek je buď zrušená objednávka (zboží zpět), nebo odeslaná
(rezervace vydaná), nikdy obojí. Hlídá to test `Soubeh_OdeslatAZrusitZaroven_VyhrajeJedenADataSedi`.

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `src/MiniShop.Domain/Orders/Events/OrderPackingStarted.cs`, `OrderShipped.cs` | doménové události expedice |
| `src/MiniShop.Contracts/Orders/OrderShippedV1.cs` | integrační událost (OrderId, e-mail, dopravce, číslo zásilky) |
| `src/MiniShop.Application/Inventory/FulfilReservationHandler.cs` | sklad: `OrderShippedV1` → rezervace vydaná |
| `src/MiniShop.Application/Ordering/EventHandlers/SendShippingNoticeHandler.cs` | e-mail o odeslání |
| `src/MiniShop.Web/Pages/Admin/Fulfilment/Index.cshtml(.cs)` | stránka Expedice: K zabalení (hromadně), K odeslání (dopravce + číslo), Na cestě (Doručeno) |
| `src/MiniShop.Web/Orders/OrderStatusLabels.cs` | texty a barvy stavů na jednom místě (osobní odběr má vlastní texty) |
| `…/Ordering/Migrations/…_Fulfilment.cs` | `Orders.Carrier`, `TrackingNumber`, `Version` |
| `…/Inventory/Migrations/…_ReservationFulfilment.cs` | `StockReservations.FulfilledAtUtc` |
| `docs/prirucka/toky/expedice.md`, `docs/kroky-eshop/krok-06-expedice.md` | dokumentace |
| `tests/MiniShop.Tests/Web/FulfilmentPageTests.cs` | stránka Expedice přes skutečné formuláře |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Orders/OrderStatus.cs` | `Packing = 5`, `Shipped = 6` (na konec – ukládá se text, stará data platí) |
| `Domain/Orders/Order.cs` | `StartPacking`, `Ship`, `MarkDelivered` (místo `Complete`), `Cancel` z Confirmed i Packing, `Carrier`, `TrackingNumber`, `Version` (zvyšuje `ChangeStatus`), `IsActive` i pro Packing/Shipped |
| `Domain/Orders/Saga/OrderSaga.cs` | stav `Shipped`; `Reserved → Shipped → Completed` |
| `Domain/Inventory/StockReservation.cs` | `FulfilledAtUtc`, `Fulfil()`; `IsActive` = ani uvolněná, ani vydaná |
| `Application/Ordering/OrderService.cs`, `IOrderService.cs`, `IOrderRepository.cs` | `GetFulfilmentQueueAsync`, `StartPackingAsync`, `ShipAsync`, `CompleteAsync` = doručeno |
| `Application/Ordering/Saga/OrderSagaOrchestrator.cs`, `IntegrationEvents/PublishOrderIntegrationEventsHandler.cs` | reakce na `OrderShipped` |
| `Application/Ordering/IOrderNotifier.cs`, `Infrastructure/Ordering/EmailOrderNotifier.cs` | `OrderShippedAsync`; „doručeno/vyzvednuto“ místo „vyřízeno“ |
| `Infrastructure/Ordering/*` | konfigurace (`Version` jako concurrency token), převod nových událostí do/z outboxu, log handler, fronty expedice v repozitáři |
| `Infrastructure/Inventory/*` | konfigurace `FulfilledAtUtc`; aktivní rezervace = ani vydané |
| `Inventory.Api/Program.cs`, `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | registrace handlerů |
| `Web/Pages/Orders/Index`, `Detail`, `Admin/Users/Detail`, `Shared/_Layout` | bez „Vyřídit“, zrušit i během balení, zásilka na detailu, menu Expedice, hláška při souběhu |
| testy | doména (přechody, verze, osobní odběr), sága, `OrderServiceTests` (celá expedice, zrušení během balení, souběh), e-mail, kontrakt, matice oprávnění |

## Rozhodnutí v tomto kroku

- **Událost, ne příkaz, pro sklad** – výdej nemůže selhat a zajímá víc odběratelů (sklad, v kroku 7 notifikace).
- **`Completed` = doručeno** – kontrakt `OrderCompletedV1` zůstal beze změny (jen význam se zpřesnil); doménová
  událost dál `OrderCompleted`, metoda `MarkDelivered`.
- **Zrušit jen do odeslání** – pravidlo v `Order.Cancel`, tlačítka na stránkách jsou jen pohodlí.
- **Osobní odběr** – stejné stavy, jiné texty („Připravujeme“, „K vyzvednutí“, „Vyzvednutá“), bez dopravce a čísla.
  Kurýr dopravce a číslo zásilky vyžaduje, objednávky bez dopravy (API) je mít nemusí.
- **Hromadné balení po jedné objednávce** – zrušená objednávka v dávce nezastaví ostatní; hláška řekne, která neprošla.
- **„Doručeno“ kliká admin** – simulace dopravce; skutečný webhook až s platební bránou (krok 8).
- **Sága nečeká na výdej** – po `OrderShipped` jde rovnou do `Shipped`, žádný příkaz ani odpověď.

## Známá omezení

- **Starší vyřízené objednávky** (před krokem 6) mají rezervaci pořád „aktivní“ – zásoba je správně, jen evidence
  výdeje chybí. Doplnit by ji šlo jen tak, že by sklad četl stav objednávek (porušení hranic modulů).
- **Potvrzené objednávky z doby před krokem 6** musí projít balením a odesláním (dřív by je admin „vyřídil“).
- Konflikt souběhu na stránce Objednávky/Expedice jen ohlásí – uživatel akci zopakuje nad aktuálním stavem.
- E-mail o odeslání odchází z Orderingu (`EmailOrderNotifier`); do modulu Notifications se přesune v kroku 7.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu `Applying migration '…_Fulfilment'`, ve skladu `'…_ReservationFulfilment'`.
2. Jako zákazník objednej kurýrem → potvrzená. Admin → **Expedice** → zaškrtnout → **Začít balit**.
3. **Odeslat** bez čísla zásilky → hláška. S dopravcem a číslem → zákazník na detailu vidí „PPL, číslo zásilky …“,
   v Mailpitu e-mail „je na cestě“; v RabbitMQ UI exchange `OrderShippedV1`.
4. V `inventory.db` má rezervace objednávky vyplněné `FulfilledAtUtc`. Zrušit objednávku už nejde.
5. **Doručeno** → detail „Objednávka doručena“, e-mail „doručená“.
6. Jiná objednávka: admin začne balit, zákazník ji zruší → zboží se vrátí na sklad, v Expedici zmizí.

### Produkce

1. Před sloučením ruční záloha: `sudo systemctl start minishop-backup` (mění se `ordering.db` i `inventory.db`).
2. Sloučit PR → CD nasadí obě služby (migrace jen přidávají sloupce).
3. https://minishop-tichy.cz → **Expedice** → projít jednu objednávku až do doručení.

## Vyzkoušejte sami

- Zakomentuj `builder.Property(o => o.Version).IsConcurrencyToken()` a spusť test souběhu. Co se stane s daty?
- Proč sklad poslouchá **událost** `OrderShippedV1`, a ne příkaz od ságy? Kdy bys naopak příkaz potřeboval?
- Přidej odběratele `OrderShippedV1` do auditní fronty – musíš měnit Ordering?
- Co by se stalo, kdyby `ReleaseStockV1` přišel až po odeslání? Najdi v kódu, co tomu brání.
