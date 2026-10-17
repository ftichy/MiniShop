# Krok 6: Idempotentní handlery (inbox)

Každý handler si teď do tabulky `ProcessedMessages` odškrtne, kterou událost už zpracoval.
Když outbox stejnou událost doručí znovu, handler ji přeskočí. Zboží se tak při opakování
nevrátí na sklad dvakrát.

## Jak to funguje

> **at-least-once doručení (outbox) + idempotentní příjemce (inbox) = efektivně právě jednou**

Přirovnání: kurýr vám může zazvonit s balíkem dvakrát. Vy si ale v sešitě odškrtáváte čísla zásilek,
a když přijde podruhé se stejným číslem, řeknete „to už mám“.

**Číslo zásilky** je nové `EventId` (Guid) v každé události. Vznikne spolu s událostí, outbox ho použije
jako `Id` řádku a worker ho vrátí do znovu sestavené události. Při každém opakování je tedy stejné.

**Sešit** je tabulka `ProcessedMessages` s klíčem (`EventId`, `Handler`). Každý handler si odškrtává sám za sebe.

**Kdo odškrtává:** dekorátor `IdempotentDomainEventHandler<T>`. Obalí každý handler, takže handlery samotné
se nezměnily:

```
dispatcher ──► IdempotentDomainEventHandler<OrderCancelled>
                 │ 1. je (EventId, ReturnStock…Handler) v ProcessedMessages? → ano: konec
                 │ 2. BEGIN TRANSACTION
                 ├──► ReturnStockOnOrderCancelledHandler   (vrátí zboží, SaveChanges – zatím nepotvrzeno)
                 │ 3. INSERT ProcessedMessages, COMMIT     ← zboží i odškrtnutí spolu, nebo nic
                 │    chyba → ROLLBACK + ChangeTracker.Clear()
```

Scénář ze simulace v kroku 5 (notifikace o zrušení selže):

| Kolo workera | Bez inboxu (krok 5) | S inboxem (krok 6) |
| --- | --- | --- |
| 1. | sklad +2, notifikace selže | sklad +2 ✔ odškrtnuto, notifikace selže |
| 2. | sklad +2 **znovu**, notifikace selže | vrácení skladu **přeskočeno**, notifikace selže |
| … 5. | celkem +10 ks | celkem +2 ks |

## Nové soubory

| Soubor | Co obsahuje |
| --- | --- |
| `Infrastructure/Inbox/ProcessedMessage.cs` | řádek inboxu (`EventId`, `Handler`, `ProcessedAtUtc`) |
| `Infrastructure/Inbox/IdempotentDomainEventHandler.cs` | dekorátor + rozhraní `IDomainEventHandlerDecorator` (pro logování skutečného handleru) |
| `Infrastructure/Persistence/Configurations/ProcessedMessageConfiguration.cs` | tabulka `ProcessedMessages`, složený klíč |
| `tests/.../Infrastructure/IdempotentHandlerTests.cs` | 4 testy dekorátoru |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Domain/Common/IDomainEvent.cs` | nová vlastnost `Guid EventId` |
| `Domain/Orders/Events/*.cs` | `EventId { get; init; } = Guid.NewGuid()` |
| `Infrastructure/Outbox/OutboxMessageFactory.cs` | `Id` řádku = `EventId` události |
| `Infrastructure/Outbox/OutboxEventReader.cs` | znovu sestavená událost dostane `EventId = message.Id` |
| `Infrastructure/Persistence/MiniShopDbContext.cs` | `DbSet<ProcessedMessage>`; když už transakce běží, nezačíná vlastní |
| `Infrastructure/DependencyInjection.cs` | nová metoda `AddDomainEventHandler<TEvent, THandler>()`, která handler vždy obalí dekorátorem |
| `Infrastructure/Events/DomainEventDispatcher.cs` | v logu chyby jméno skutečného handleru, ne dekorátoru |
| `Web/Extensions/ApplicationServiceCollectionExtensions.cs` | handlery se registrují přes `AddDomainEventHandler` |
| `Application/Orders/EventHandlers/ReturnStockOnOrderCancelledHandler.cs` | jen komentář |
| `tests/.../TestDoubles/RecordingOrderNotifier.cs` | `CancellationFailuresLeft` – simuluje nedostupný e-mailový server |
| `tests/.../Application/OrderServiceTests.cs` | registrace přes `AddDomainEventHandler`; nový test opakování |

## Rozhodnutí v tomto kroku

- **Dekorátor místo kódu v každém handleru.** Kontrola duplicit je na jednom místě a handlery řeší jen svou práci.
  Přidáváme chování bez úpravy existujícího kódu (Open/Closed).
- **Registrace jen přes `AddDomainEventHandler`.** Handler nejde snadno zaregistrovat bez ochrany.
  Díky tomu `AddApplication` ve Webu používá metodu z infrastruktury. To je v pořádku, Web je composition root.
- **Transakce kolem handleru i odškrtnutí.** Handler sám volá `SaveChangesAsync`. Uloží se do transakce
  dekorátoru a potvrdí se až spolu se záznamem v inboxu. `MiniShopDbContext` proto vlastní transakci
  začíná jen tehdy, když žádná neběží.
- **Při chybě `ChangeTracker.Clear()`.** Všechny handlery jedné události sdílejí jeden DbContext. Bez vyčištění
  by rozpracované změny selhaného handleru uložil až další handler.
- **Odškrtává se plný název třídy handleru** (`MiniShop.Application.Orders.EventHandlers.…`).
  Přejmenování nebo přesun handleru znamená, že staré záznamy přestanou platit. U zpráv v outboxu,
  které čekají na opakování, by pak handler proběhl znovu.
- **Kontrola „už mám?“ a zápis nejsou zamčené.** Worker běží v jedné instanci, takže to stačí.
  Kdyby se dva pokusy přece jen potkaly, složený primární klíč druhý zápis odmítne a jeho transakce se vrátí.

## Známá omezení

- **Vnější akce nejsou v transakci.** Když se e-mail odešle a aplikace spadne před zápisem do inboxu,
  odejde při opakování znovu. Pro změny v databázi (sklad) platí „právě jednou“ stoprocentně, pro e-mail ne.
  V praxi se `EventId` předá i e-mailové službě, aby duplicitu poznala sama.
- **Inbox roste donekonečna.** Mazání starých záznamů (např. starších než 30 dní) zatím není.
- **Worker pořád počítá s jednou instancí aplikace** a opakuje bez backoffu (viz krok 5).

⚠️ Přibyla tabulka `ProcessedMessages` – před spuštěním smažte `src/MiniShop.Web/minishop.db`.

## Jak ověřit

1. `dotnet test` – projde všech 42 testů, z toho 5 nových (4× `IdempotentHandlerTests`,
   `Cancel_NotifikacePoprveSelze_PriOpakovaniSeZboziNevratiDvakrat`).
2. Smažte `minishop.db`, spusťte aplikaci, vytvořte objednávku a zrušte ji.
3. V DB Browseru for SQLite uvidíte v `ProcessedMessages` pět řádků: dva s `EventId` řádku `OrderPlaced`
   (potvrzení + log) a tři s `EventId` řádku `OrderCancelled` (vrácení skladu, oznámení, log).

## Vyzkoušejte sami

Zopakujte simulaci z kroku 5: do `LoggingOrderNotifier.OrderCancelledAsync` dejte na první řádek
`throw new InvalidOperationException("E-mailový server nedostupný");` a zrušte objednávku.
Worker zprávu zkusí pětkrát, ale **zboží se vrátí jen jednou**. V `ProcessedMessages` přitom uvidíte záznam
`ReturnStockOnOrderCancelledHandler`, ale ne `SendCancellationNoticeHandler`.
Pak `throw` smažte, restartujte aplikaci a v DB nastavte `Attempts = 0`. Notifikace odejde
a vrácení skladu se tentokrát přeskočí.
