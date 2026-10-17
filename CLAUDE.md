# MiniShop – instrukce pro Claude

## Komunikace
- S uživatelem mluv **česky** (vysvětlení, souhrny, komentáře v kódu, commit zprávy).
- Uživatel je vývojář, ale v event-driven architektuře (EDA) je **začátečník**. Vysvětluj jednoduše,
  na konkrétních místech v kódu, s přirovnáním ze života, když je to užitečné.
- Identifikátory v kódu (třídy, metody, proměnné) piš **anglicky**, komentáře a texty pro uživatele česky.

## Projekt
Výukový e-shop: ASP.NET Core (.NET 10), EF Core + SQLite, REST API + Razor Pages, xUnit.
Cíl: postupně ho převést na EDA podle plánu v `docs/EDA-roadmap.md`.

```
src/MiniShop.Domain          entity (Product, Order, OrderItem), doménové události, žádné závislosti
src/MiniShop.Application     služby, rozhraní (repozitáře, IUnitOfWork, IOrderNotifier, IDomainEvent*), handlery událostí
src/MiniShop.Infrastructure  EF Core, repozitáře, DomainEventDispatcher, Outbox, LoggingOrderNotifier
src/MiniShop.Web             API controllery, Razor Pages, Program.cs (composition root)
tests/MiniShop.Tests         testy domény, OrderService (SQLite in-memory), dispatcheru, outboxu
docs/                        EDA-roadmap.md, kroky/ (souhrny hotových kroků), diagramy/
```

## Příkazy
- Build: `dotnet build MiniShop.slnx`
- Testy: `dotnet test MiniShop.slnx`
- Spuštění: `dotnet run --project src/MiniShop.Web` (https://localhost:7180)
- Po každé změně kódu spusť build a testy a oprav chyby, než ohlásíš hotovo.

## Konvence a záměrná rozhodnutí
- Závislosti vedou dovnitř: Web → Application → Domain, Infrastructure → Application.
- `MiniShop.Application` **nemá žádné NuGet balíčky** (ani DI ani Logging) – předchází se tím konfliktu verzí
  (NU1605). Registrace aplikačních služeb a handlerů proto žije ve
  `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs`, infrastruktury v
  `src/MiniShop.Infrastructure/DependencyInjection.cs`.
- Entity mění stav jen přes metody s business pravidly; čas se předává parametrem (z `TimeProvider`),
  doména nevolá `DateTime.UtcNow`.
- Databáze vzniká přes `EnsureCreated` (bez migrací). **Při změně schématu** (nová tabulka/sloupec)
  je potřeba smazat `src/MiniShop.Web/minishop.db` – upozorni na to uživatele.
- Testy OrderService používají skutečný dispatcher a skutečné handlery (testují celou cestu).

## Stav EDA (viz `docs/EDA-roadmap.md`)
| Krok | Stav | Souhrn |
| --- | --- | --- |
| 1. Doménové události (`Entity`, `IDomainEvent`, `OrderPlaced/Completed/Cancelled`) | hotovo | — |
| 2. In-process dispatcher (`DomainEventDispatcher`, dispatch po `SaveChangesAsync`) | hotovo | `docs/kroky/krok-02-dispatcher.md` |
| 3. Handlery místo přímých volání v `OrderService` | hotovo | `docs/kroky/krok-03-handlery.md` |
| 4. Outbox pattern (`OutboxMessages` v téže transakci, dispatch hned po commitu) | hotovo | `docs/kroky/krok-04-outbox.md` |
| 5. Background worker pro outbox | **další na řadě** | |
| 6.–11. | čeká | |

Známé omezení po kroku 4: řádky outboxu se `Error` a `ProcessedAtUtc = NULL` nikdo znovu nezpracuje
(krok 5) a opakování by spustilo i handlery, které už uspěly (krok 6). Payload v outboxu je JSON
`OrderPayload` – worker ho bude muset převést zpět na doménovou událost (např. načtením `Order` podle `OrderId`).

## Postup práce na dalším kroku EDA
1. Nejdřív **shrň**, co se bude přidávat/měnit a **proč** (teorie + konkrétní případ v MiniShopu),
   a počkej na souhlas uživatele.
2. Proveď změny po malých částech; drž se stylu existujícího kódu.
3. `dotnet build` + `dotnet test`, oprav chyby.
4. Napiš souhrn do `docs/kroky/krok-NN-nazev.md` ve stejném formátu jako předchozí
   (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami).
5. Aktualizuj tabulku „Stav EDA“ v tomto souboru.
6. Necommituj – commit dělá uživatel sám.
