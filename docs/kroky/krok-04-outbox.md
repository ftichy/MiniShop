# Krok 4: Outbox pattern

Každá doménová událost se teď ukládá jako řádek do tabulky `OutboxMessages`, a to **ve stejné
transakci** jako změna objednávky. Událost se tak už nemůže ztratit: buď se uloží data i událost, nebo nic.

## Jak to funguje

Přirovnání: dopis vložíte do schránky „odchozí“ na stole a hned ho zkusíte odnést na poštu.
Když je zavřeno, dopis zůstane ve schránce a nezapomene se na něj. Vyzvedne ho pošťák (worker v kroku 5).

`MiniShopDbContext.SaveChangesAsync` teď dělá tohle:

```
BEGIN TRANSACTION
  1. uloží entity                     → objednávka dostane ID
  2. převede události na řádky outboxu (JSON jen s daty)
  3. uloží řádky outboxu
COMMIT                                ← od teď je událost v bezpečí
  4. rozešle události handlerům (jako v kroku 3)
  5. úspěch → ProcessedAtUtc = teď
     chyba  → Error = text chyby, ProcessedAtUtc zůstane NULL
```

Pro zrušení objednávky to vypadá takto:

| Co se pokazí | Před krokem 4 | Po kroku 4 |
| --- | --- | --- |
| Handler vrácení skladu vyhodí výjimku | zboží se nevrátí, zůstane jen řádek v logu | zboží se zatím nevrátí, ale v DB je řádek `OrderCancelled` s chybou |
| Aplikace spadne hned po uložení objednávky | událost je navždy pryč | řádek `OrderCancelled` s `ProcessedAtUtc = NULL` čeká v DB |
| Selže samotné uložení | nic se neuloží | nic se neuloží (ani řádek outboxu) |

Řádek v outboxu vypadá například takto:

```
Type:           OrderCancelled
Payload:        {"OrderId":1,"CustomerEmail":"jan@example.com","TotalPrice":15980.0,
                 "Lines":[{"ProductId":3,"Quantity":2}]}
ProcessedAtUtc: 2026-10-17 14:29:42
Error:          NULL
```

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `Infrastructure/Outbox/OutboxMessage.cs` | entita řádku outboxu (`Type`, `Payload`, `OccurredAtUtc`, `ProcessedAtUtc`, `Error`) |
| `Infrastructure/Outbox/OutboxMessageFactory.cs` | převod události na řádek; payload `OrderPayload` + `OrderLinePayload` |
| `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` | tabulka `OutboxMessages`, UTC převod časů, index na `ProcessedAtUtc` |
| `tests/.../Infrastructure/OutboxTests.cs` | 4 testy outboxu |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/Persistence/MiniShopDbContext.cs` | `DbSet<OutboxMessage>`, transakce, zápis do outboxu, označení zpracování; nový parametr `TimeProvider` |
| `Infrastructure/Events/DomainEventDispatcher.cs` | po doběhnutí všech handlerů vyhodí `AggregateException`, pokud některý selhal |
| `Application/Abstractions/IDomainEventDispatcher.cs` | komentář k novému chování při chybě |
| `Infrastructure/DependencyInjection.cs` | `TryAddSingleton(TimeProvider.System)` pro DbContext |
| `Application/Orders/EventHandlers/ReturnStockOnOrderCancelledHandler.cs` | jen komentář: co se děje při chybě |
| `tests/.../Infrastructure/DomainEventDispatcherTests.cs` | test chyby handleru ověřuje i nahlášení výjimky |
| `tests/.../Application/OrderServiceTests.cs` | nový test outboxu při zrušení; kontrola prázdného outboxu při neúspěchu |

## Rozhodnutí v tomto kroku

- **Události se dál doručují hned po commitu.** Roadmapa nechává doručení až na worker z kroku 5.
  Bez toho by ale aplikace mezi krokem 4 a 5 přestala vracet zboží a posílat notifikace.
  Outbox tu zatím slouží jako pojistka a záznam, worker pak dozpracuje to, co zůstalo.
- **Transakce kolem dvou uložení.** Payload potřebuje ID objednávky a to vzniká až při prvním uložení.
  Proto uložíme entity, pak outbox, a teprve potom commit. Navenek je to jedna atomická operace.
- **Payload jsou jen data.** JSON nemůže nést odkaz na entitu, proto `OrderId`, e-mail, cena a položky.
  Doménové události a handlery se nemění. Neznámá událost vyhodí výjimku a vrátí celou transakci,
  takže se nestane, že by se nějaká událost tiše neuložila.
- **Dispatcher chybu nahlásí, ale nepřeruší ostatní handlery.** Notifikace odejde, i když vrácení skladu selže.
  Výjimka se nepropaguje k uživateli – data jsou uložená a chyba je zapsaná v outboxu.
- **Značka zpracování přes `ExecuteUpdate`.** Zápis jde přímo do DB mimo change tracker. Obyčejné `SaveChanges`
  by spolu se značkou uložilo i rozpracované změny handleru, který právě selhal.
- **Uložení bez událostí** (např. úprava produktu) jde postaru, bez explicitní transakce a bez outboxu.

## Známá omezení

- **Nezpracované řádky zatím nikdo znovu nezkouší.** Řádek s `Error` v DB zůstane, ale sklad se sám nevrátí.
  Opakování přinese worker na pozadí v kroku 5.
- **Opakování spustí všechny handlery události znovu.** Když selže jen vrácení skladu, při opakování
  se pošle i notifikace podruhé. Proti tomu chrání idempotence (inbox) v kroku 6.
- **Handlery stále běží uvnitř HTTP požadavku**, takže pomalý handler zdrží odpověď. Asynchronnost přinese krok 5.

⚠️ Přibyla tabulka `OutboxMessages` – před spuštěním smažte `src/MiniShop.Web/minishop.db`
(vznikne znovu přes `EnsureCreated`).

## Jak ověřit

1. `dotnet test` – projde všech 33 testů, z toho 5 nových
   (4× `OutboxTests`, `Cancel_UdalostVOutboxuJeZpracovanaAZboziVraceno`).
2. Smažte `minishop.db`, spusťte aplikaci, vytvořte objednávku a zrušte ji.
3. Otevřete `minishop.db` v DB Browseru for SQLite: tabulka `OutboxMessages` obsahuje řádky
   `OrderPlaced` a `OrderCancelled` s vyplněným `ProcessedAtUtc` a prázdným `Error`.

## Vyzkoušejte sami

Stejný pokus jako v kroku 3: do `ReturnStockOnOrderCancelledHandler.HandleAsync` dejte na první řádek
`throw new Exception("test");` a zrušte objednávku. Objednávka se zruší, notifikace odejde a sklad se nevrátí –
**ale** v `OutboxMessages` teď uvidíte řádek `OrderCancelled` s `ProcessedAtUtc = NULL` a `Error` obsahujícím „test“.
Rozdíl proti kroku 3: chyba už není jen v logu, který nikdo nečte, ale v databázi jako úkol „dodělat“.
Pak řádek zase smažte.
