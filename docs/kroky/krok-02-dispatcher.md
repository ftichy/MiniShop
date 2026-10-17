# Krok 2: In-process dispatcher a handlery

Události, které entity vyvolávají od kroku 1, se teď po uložení do databáze doručí handlerům.
Funguje to celé uvnitř jednoho procesu, bez sítě a bez externí knihovny.

## Jak to funguje

```
OrderService.PlaceOrderAsync
  └─ new Order(...)                  → entita si zapíše OrderPlaced do DomainEvents
  └─ unitOfWork.SaveChangesAsync()   = MiniShopDbContext.SaveChangesAsync
        1. posbírá DomainEvents ze všech sledovaných entit (Order, Product)
        2. vyčistí je na entitách
        3. base.SaveChangesAsync()   → zápis do SQLite, objednávka dostane ID
        4. IDomainEventDispatcher.DispatchAsync(události)
              └─ pro OrderPlaced najde v DI všechny IDomainEventHandler<OrderPlaced>
                    └─ OrderEventsLogHandler → log "[Událost] OrderPlaced: objednávka 7 …"
```

## Nové soubory

| Soubor | Vrstva | Účel |
| --- | --- | --- |
| `Application/Abstractions/IDomainEventHandler.cs` | Application | Rozhraní handleru jedné události |
| `Application/Abstractions/IDomainEventDispatcher.cs` | Application | Rozhraní dispatcheru |
| `Infrastructure/Events/DomainEventDispatcher.cs` | Infrastructure | Najde handlery v DI a zavolá je |
| `Infrastructure/Events/Handlers/OrderEventsLogHandler.cs` | Infrastructure | Pokusný handler, loguje všechny tři události objednávky |
| `tests/.../Infrastructure/DomainEventDispatcherTests.cs` | Testy | 3 testy dispatcheru |
| `tests/.../TestDoubles/RecordingDomainEventDispatcher.cs` | Testy | Testovací dispatcher, který si události jen pamatuje |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/Persistence/MiniShopDbContext.cs` | Nový parametr `IDomainEventDispatcher` v konstruktoru; přepsaná `SaveChangesAsync` (sběr → uložení → dispatch) |
| `Infrastructure/DependencyInjection.cs` | Registrace dispatcheru a handleru pro `OrderPlaced`, `OrderCompleted`, `OrderCancelled` |
| `tests/.../Application/OrderServiceTests.cs` | Kontext dostává testovací dispatcher; 3 nové testy (dispatch po uložení, zrušení, žádný dispatch při chybě) |

`OrderService`, doména ani UI se nezměnily. To je cíl: kód, který událost vyvolá, neví nic o tom, kdo na ni reaguje.

## Rozhodnutí v tomto kroku

- **Dispatch až po uložení.** Handler vidí uložená data a ID objednávky. Cena za to: když aplikace spadne mezi uložením a dispatchem, událost se ztratí.
- **Chyba v handleru nezastaví ostatní.** Dispatcher výjimku zaloguje a pokračuje dalším handlerem, protože data už jsou uložená a HTTP požadavek by neměl spadnout kvůli vedlejšímu efektu. Stejně jako v předchozím bodě se tím ale událost pro daný handler ztratí.
- **Vlastní dispatcher místo knihovny MediatR.** Je to asi 30 řádků a je v nich vidět celý princip. Generický `HandlerInvoker<TEvent>` volá handlery typově bezpečně a bez reflexe při každém volání.
- **Scoped životnost.** Dispatcher i handlery žijí v rámci jednoho HTTP požadavku, takže handler sdílí stejný `DbContext`.
- **Události se mažou před dispatchem.** Když handler sám zavolá `SaveChangesAsync`, stejné události se znovu nerozešlou.

Obě ztráty událostí z prvních dvou bodů řeší **krok 4 (outbox)** a **krok 5 (background worker s opakováním)**.

## Jak ověřit

1. `dotnet test` – projdou původní testy i 6 nových.
2. Spusťte aplikaci, vytvořte objednávku a pak ji vyřiďte nebo zrušte. V terminálu uvidíte řádky jako:
   ```
   info: MiniShop.Infrastructure.Events.Handlers.OrderEventsLogHandler[0]
         [Událost] OrderPlaced: objednávka 3, 1 položek, celkem 2 490,00 Kč
   ```
   Hned vedle je i původní log z `LoggingOrderNotifier`. Ten v kroku 3 nahradí handler.

## Vyzkoušejte sami

Přidejte vlastní handler, například `CountOrdersHandler : IDomainEventHandler<OrderPlaced>`, který loguje celkový počet objednávek. Zaregistrujte ho v `DependencyInjection.cs`. `OrderService` přitom neměníte, a to je Open/Closed princip v praxi.
