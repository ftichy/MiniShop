---
title: SOLID v kódu
summary: Pět principů objektového návrhu a konkrétní místa v MiniShopu, kde je uvidíte.
order: 5
---

> **V jedné větě:** SOLID je pět pravidel, díky kterým jde kód **měnit bez strachu** – nová funkce znamená
> spíš přidat třídu než přepisovat existující.

| Písmeno | Princip | Jednoduše |
| --- | --- | --- |
| **S** | Single Responsibility | Třída má jeden důvod ke změně. |
| **O** | Open/Closed | Otevřené pro rozšíření, uzavřené pro úpravy. |
| **L** | Liskov Substitution | Implementaci rozhraní jde zaměnit za jinou, aniž by se program rozbil. |
| **I** | Interface Segregation | Malá rozhraní na míru, ne jedno obří. |
| **D** | Dependency Inversion | Business logika závisí na abstrakcích, ne na databázi a síti. |

## S – Single Responsibility

**Kde:** [OrderService](code:src/MiniShop.Application/Ordering/OrderService.cs) a handlery v
[EventHandlers/](code:src/MiniShop.Application/Ordering/EventHandlers/).

Původně (krok 1) `OrderService` při zrušení objednávky sám vracel zboží na sklad **a** posílal oznámení. Měl tak tři
důvody ke změně. Od kroku 3 jen změní objednávku a uloží ji; vrácení zboží řeší sága a oznámení samostatné handlery
(`SendCancellationNoticeHandler`…). Každý dělá jednu věc.

> **Přirovnání:** Číšník objednávku přijme a předá do kuchyně. Nevaří, neumývá nádobí a neúčtuje zásoby.

## O – Open/Closed

**Kde:** [IDiscountPolicy](code:src/MiniShop.Application/Ordering/Pricing/IDiscountPolicy.cs) a doménové události.

- Nová sleva = nová třída implementující `IDiscountPolicy` a jeden řádek registrace. `OrderService` si vezme
  všechna pravidla a použije nejvýhodnější – **sám se nemění**.
- Nová reakce na objednávku (např. věrnostní body) = nový handler `IDomainEventHandler<OrderConfirmed>`.
  `Order` ani `OrderService` se o něm nedozví.
- [IdempotentDomainEventHandler](code:src/MiniShop.Infrastructure/Inbox/IdempotentDomainEventHandler.cs) přidává
  ochranu proti duplicitám jako **dekorátor** – obalí handler, aniž by se handler měnil.

## L – Liskov Substitution

**Kde:** `IIntegrationBus` a `IOrderNotifier`.

- [RabbitMqIntegrationBus](code:src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqIntegrationBus.cs) a
  [InProcessIntegrationBus](code:src/MiniShop.Infrastructure/Integration/InProcessIntegrationBus.cs) jsou
  zaměnitelné. Testy běží celou ságu v procesu, produkce přes RabbitMQ – handlery nepoznají rozdíl.
- Oznámení zákazníkovi: původní `LoggingOrderNotifier` (jen log) nahradil v účtech kroku 5
  [EmailOrderNotifier](code:src/MiniShop.Infrastructure/Ordering/EmailOrderNotifier.cs) – handlery ani `OrderService`
  se nezměnily ani o řádek. Stejně tak `IEmailSender`: SMTP, nebo jen log, když server není nastavený.

## I – Interface Segregation

**Kde:** rozhraní modulů v [Application/Ordering](code:src/MiniShop.Application/Ordering/) a
[Application/Inventory](code:src/MiniShop.Application/Inventory/).

Místo jednoho obecného „repozitáře všeho“ má každý modul úzká rozhraní: `IOrderRepository`,
`ICatalogProductRepository`, `IOrderingUnitOfWork`, `IOrderingMessagePublisher`… Handler skladu dostane
`IInventoryUnitOfWork` – **nemůže** omylem uložit do databáze objednávek, protože k ní nemá rozhraní.

## D – Dependency Inversion

**Kde:** konstruktory služeb a [vrstvy](vrstvy.md) projektu.

```csharp
public sealed class OrderService(
    IOrderRepository orders,               // ne OrderRepository / EF Core
    ICatalogProductRepository catalog,
    IOrderingUnitOfWork unitOfWork,        // ne OrderingDbContext
    IEnumerable<IDiscountPolicy> discountPolicies,
    TimeProvider timeProvider)             // ne DateTime.UtcNow
```

Application definuje, **co potřebuje**; Infrastructure to dodá. I čas je abstrakce (`TimeProvider`) – testy ho
umí posunout (`FixedTimeProvider.Advance`) a vyzkoušet třeba timeout ságy bez čekání.

## Kdy SOLID nepřehánět

Každé rozhraní je další soubor a další skok při čtení kódu. MiniShop má rozhraní tam, kde se implementace
**skutečně mění** (testy vs. produkce, RabbitMQ vs. paměť) nebo kde chrání hranici modulu.

## Na internetu

- [Microsoft: Architektonické principy](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles)
- [SOLID (Wikipedie)](https://en.wikipedia.org/wiki/SOLID)
- [Refactoring.Guru: Decorator](https://refactoring.guru/design-patterns/decorator)
