# Krok 3: Rozdělení OrderService na handlery

`OrderService` teď řeší jen objednávku samotnou. Notifikace a vrácení zboží při zrušení
převzaly handlery doménových událostí z kroku 2.

## Před a po

**Před** – `CancelAsync` dělala čtyři věci:

```csharp
order.Cancel(...);                          // 1. změna objednávky
foreach (...) product.AddStock(...);        // 2. vrácení skladu
await unitOfWork.SaveChangesAsync();        // 3. uložení
await notifier.OrderCancelledAsync(order);  // 4. notifikace
```

**Po** – dělá jen to, co jí patří:

```csharp
order.Cancel(UtcNow);                       // vyvolá OrderCancelled
await unitOfWork.SaveChangesAsync();        // uloží → dispatcher → handlery
```

Kdo na zrušení reaguje, poznáte z registrací, ne z kódu `OrderService`:

```
OrderCancelled ─┬─ ReturnStockOnOrderCancelledHandler   vrátí zboží na sklad
                ├─ SendCancellationNoticeHandler        oznámí zákazníkovi
                └─ OrderEventsLogHandler                zapíše do deníku událostí
```

## Nové soubory

| Soubor | Poslouchá | Co dělá |
| --- | --- | --- |
| `Application/Orders/EventHandlers/SendOrderConfirmationHandler.cs` | `OrderPlaced` | potvrzení zákazníkovi přes `IOrderNotifier` |
| `Application/Orders/EventHandlers/SendCompletionNoticeHandler.cs` | `OrderCompleted` | oznámení o vyřízení |
| `Application/Orders/EventHandlers/SendCancellationNoticeHandler.cs` | `OrderCancelled` | oznámení o zrušení |
| `Application/Orders/EventHandlers/ReturnStockOnOrderCancelledHandler.cs` | `OrderCancelled` | vrátí zboží na sklad a uloží |

Handlery jsou v aplikační vrstvě, protože obsahují business logiku („po zrušení se zboží vrací na sklad“).
`OrderEventsLogHandler` v infrastruktuře zůstává jako technický deník událostí.

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Application/Services/OrderService.cs` | z konstruktoru zmizel `IOrderNotifier`; `CancelAsync` už nevrací sklad; žádné volání notifikací |
| `Application/Abstractions/IOrderNotifier.cs` | jen komentář: rozhraní teď volají handlery |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | registrace 4 nových handlerů |
| `tests/.../TestDoubles/RecordingDomainEventDispatcher.cs` | umí události předat i skutečnému dispatcheru |
| `tests/.../Application/OrderServiceTests.cs` | používá skutečný dispatcher a handlery; 2 nové testy |

## Rozhodnutí v tomto kroku

- **`IOrderNotifier` zůstal.** Handler rozhoduje *kdy* poslat zprávu, notifier *jak* (dnes log, později e-mail). Každý tak má jednu odpovědnost.
- **Odečtení skladu při vytvoření objednávky zůstalo v `PlaceOrderAsync`.** Kontrola „je dost zboží?“ musí proběhnout *před* přijetím objednávky. Asynchronní rezervaci přinese sága v kroku 9.
- **Jeden handler = jedna třída.** Na rozdíl od `OrderEventsLogHandler` dělá každý handler něco jiného, proto samostatné třídy.
- **Testy jdou celou cestou.** `OrderServiceTests` skládá stejné handlery jako aplikace a ověřuje výsledek (sklad, notifikace), ne jen to, že se zavolala metoda.

## Známé omezení (vyřeší krok 4)

Vrácení skladu teď běží **po** uložení zrušené objednávky, ve **druhém** `SaveChangesAsync`.
Když handler selže nebo aplikace spadne mezi těmito dvěma uloženími, objednávka zůstane zrušená,
ale zboží se na sklad nevrátí. Dispatcher chybu jen zaloguje. To je **dual-write problém** a řeší ho outbox.

## Jak ověřit

1. `dotnet test` – projdou všechny testy včetně 2 nových
   (`Complete_PosleOznameniPresHandler`, `Cancel_SmazanyProdukt_HandlerHoPreskoci`).
2. Spusťte aplikaci, vytvořte objednávku a zrušte ji. Na stránce Produkty se zásoba vrátí
   a v terminálu uvidíte log z `LoggingOrderNotifier` i řádek `[Událost] OrderCancelled`.

## Vyzkoušejte sami

Do `ReturnStockOnOrderCancelledHandler.HandleAsync` dejte na první řádek
`throw new Exception("test");` a zrušte objednávku. Objednávka se zruší, notifikace odejde,
ale sklad se nevrátí a v logu bude chyba. Takhle vypadá problém, který vyřeší krok 4.
Pak řádek zase smažte.
