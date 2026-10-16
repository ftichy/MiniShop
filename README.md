# MiniShop

Jednoduchý e-shop pro výuku: **ASP.NET Core (.NET 10)**, **EF Core + SQLite**, REST API + Razor Pages,
vrstvená architektura a principy **SOLID**. Je připravený na pozdější přestavbu na **event-driven architekturu (EDA)**.

## Co aplikace umí

- **Produkty**: katalog, přidání, úprava, naskladnění, smazání
- **Objednávky**: vytvoření (odečte zboží ze skladu), vyřízení, zrušení (vrátí zboží na sklad)
- **Množstevní sleva**: od 10 ks jednoho produktu je sleva 10 %
- Webové UI (`/Products`, `/Orders`) i REST API (`/api/...`)

## Spuštění

**VS Code:** otevřete složku `MiniShop` (File → Open Folder) a nainstalujte doporučená rozšíření
(C# Dev Kit, REST Client). Spuštění s debuggerem: F5 a vyberte konfiguraci „MiniShop.Web“.
Build `Ctrl+Shift+B`, testy přes Test Explorer nebo úlohu `test`, živé přenačítání úlohou `watch`.

**Visual Studio:** otevřete `MiniShop.slnx`, nastavte `MiniShop.Web` jako startovací projekt a stiskněte F5.

**Příkazová řádka:**

```powershell
dotnet run --project src/MiniShop.Web
dotnet test
```

Aplikace poběží na `https://localhost:7180`. Databáze `minishop.db` se vytvoří sama při prvním spuštění
(ve složce `src/MiniShop.Web`) a naplní se ukázkovými produkty. Když ji smažete, vytvoří se znovu.

Ukázkové API požadavky jsou v souboru `src/MiniShop.Web/MiniShop.Web.http`. Ve VS Code (rozšíření REST Client)
i ve Visual Studiu stačí nad požadavkem kliknout na „Send Request“. OpenAPI popis najdete na `/openapi/v1.json`.

## API

| Metoda | Cesta | Popis |
|---|---|---|
| GET | `/api/products` | seznam produktů |
| GET | `/api/products/{id}` | detail produktu |
| POST | `/api/products` | nový produkt |
| PUT | `/api/products/{id}` | úprava produktu |
| POST | `/api/products/{id}/stock` | naskladnění `{ "quantity": 10 }` |
| DELETE | `/api/products/{id}` | smazání produktu |
| GET | `/api/orders` | seznam objednávek |
| GET | `/api/orders/{id}` | detail objednávky |
| POST | `/api/orders` | nová objednávka |
| POST | `/api/orders/{id}/complete` | vyřízení |
| POST | `/api/orders/{id}/cancel` | zrušení |

Chyby se vracejí jako `ProblemDetails`: 400 pro porušení business pravidla, 404 když něco neexistuje.

## Struktura řešení

```
src/
  MiniShop.Domain          Entity a business pravidla (Product, Order, OrderItem). Bez závislostí.
  MiniShop.Application     Use-casy (ProductService, OrderService), rozhraní, DTO, slevová pravidla.
  MiniShop.Infrastructure  EF Core + SQLite, repozitáře, notifikace (zatím jen do logu).
  MiniShop.Web             API controllery, Razor Pages, zpracování chyb, skládání DI.
tests/
  MiniShop.Tests           xUnit testy domény a OrderService (SQLite v paměti).
```

Závislosti vedou jen dovnitř: `Web → Application → Domain` a `Infrastructure → Application`.
Aplikační vrstva definuje rozhraní (`IProductRepository`, `IUnitOfWork`, `IOrderNotifier`) a infrastruktura je implementuje.

## Kde v kódu najdete SOLID

| Princip | Příklad |
|---|---|
| **S** – Single Responsibility | Controller jen přijme HTTP požadavek, `OrderService` řeší use-case, repozitář přístup k datům, `GlobalExceptionHandler` převod chyb na HTTP odpovědi. |
| **O** – Open/Closed | Slevy přes `IDiscountPolicy`. Nové pravidlo = nová třída zaregistrovaná v DI, `OrderService` se nemění. |
| **L** – Liskov Substitution | `OrderService` funguje stejně s `LoggingOrderNotifier` i s testovacím `RecordingOrderNotifier`, a stejně tak s jakoukoli implementací repozitáře. |
| **I** – Interface Segregation | Malá rozhraní: zvlášť `IProductRepository`, `IOrderRepository`, `IUnitOfWork`, `IOrderNotifier`. |
| **D** – Dependency Inversion | Služby závisí na abstrakcích (včetně `TimeProvider` místo `DateTime.UtcNow`). Konkrétní třídy se skládají v `Program.cs`. |

Doména je zapouzdřená: stav se mění jen přes metody (`RemoveStock`, `Cancel`, ...), které hlídají pravidla.

## Výhled: přechod na EDA

Místa, která se při přestavbě změní, jsou v kódu označená komentáři:

1. `OrderService.PlaceOrderAsync` teď dělá vše najednou: objednávku, odečtení skladu a notifikaci.
   V EDA vznikne jen objednávka a publikuje se událost `OrderPlaced`.
2. Sklad začne reagovat na `OrderPlaced` vlastním handlerem (rezervace zboží → `StockReserved` / `StockReservationFailed`).
3. Z `IOrderNotifier` se stane publisher událostí. Pro spolehlivé doručení přidáme **Outbox pattern**.
4. `OrderItem` už teď nemá cizí klíč na produkt, takže objednávky a katalog jdou později oddělit.

Doporučené pořadí: doménové události v procesu (in-memory) → outbox → message broker (např. RabbitMQ) → oddělení služeb.
