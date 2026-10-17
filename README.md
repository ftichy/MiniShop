# MiniShop

Jednoduchý e-shop pro výuku: **ASP.NET Core (.NET 10)**, **EF Core + SQLite**, REST API + Razor Pages,
vrstvená architektura a principy **SOLID**. Je připravený na pozdější přestavbu na **event-driven architekturu (EDA)**.

## Co aplikace umí

- **Produkty**: katalog, přidání, úprava, naskladnění, smazání
- **Objednávky**: vytvoření (odečte zboží ze skladu), vyřízení, zrušení (vrátí zboží na sklad)
- **Množstevní sleva**: od 10 ks jednoho produktu je sleva 10 %
- Webové UI (`/Products`, `/Orders`) i REST API (`/api/...`)

## Spuštění

Aplikace se skládá ze dvou služeb a RabbitMQ (od kroku 10B):

| Služba | Adresa | Co dělá |
|---|---|---|
| `MiniShop.Web` | `https://localhost:7180` | objednávky (sága, kopie katalogu) + celé UI |
| `MiniShop.Inventory.Api` | `https://localhost:7181` | produkty, zásoby, rezervace (`/api/products`) |
| RabbitMQ | – | zprávy mezi službami |

Potřebujete běžící **Docker** (RabbitMQ v kontejneru).

**Doporučeno – .NET Aspire** spustí všechno najednou a otevře dashboard (stav služeb, logy, odkazy):

```powershell
dotnet run --project src/MiniShop.AppHost
dotnet test
```

**VS Code:** otevřete složku `MiniShop` (File → Open Folder) a nainstalujte doporučená rozšíření
(C# Dev Kit, REST Client). Spuštění s debuggerem: F5 a vyberte konfiguraci „MiniShop.AppHost (Aspire)“.
Build `Ctrl+Shift+B`, testy přes Test Explorer nebo úlohu `test`.

**Visual Studio:** otevřete `MiniShop.slnx`, nastavte `MiniShop.AppHost` jako startovací projekt a stiskněte F5.

**Bez Aspire:** spusťte RabbitMQ (`docker start minishop-rabbit`) a obě služby ve dvou terminálech:

```powershell
dotnet run --project src/MiniShop.Inventory.Api
dotnet run --project src/MiniShop.Web
```

Databáze `ordering.db` (ve `src/MiniShop.Web`) a `inventory.db` (ve `src/MiniShop.Inventory.Api`) se vytvoří
samy při prvním spuštění, sklad se naplní ukázkovými produkty. Když je smažete, vytvoří se znovu.

Ukázkové API požadavky jsou v souboru `src/MiniShop.Web/MiniShop.Web.http`. Ve VS Code (rozšíření REST Client)
i ve Visual Studiu stačí nad požadavkem kliknout na „Send Request“. OpenAPI popis najdete na `/openapi/v1.json`
(u každé služby).

## API

Produkty obsluhuje `MiniShop.Inventory.Api` (port 7181), objednávky `MiniShop.Web` (port 7180).

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
