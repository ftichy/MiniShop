# Krok 5: Background worker pro outbox

Události už se nerozesílají uvnitř HTTP požadavku. Požadavek je jen uloží do outboxu a skončí.
Doručení obstará **worker na pozadí**, který každé 2 sekundy projde nezpracované zprávy.
Když handler selže, worker to zkusí znovu. Po 5 neúspěších to vzdá a zpráva čeká na člověka.

## Jak to funguje

Přirovnání: dopis už nenesete na poštu sami. Hodíte ho do schránky „odchozí“ a jdete dělat další práci.
Pošťák chodí každé 2 sekundy. Když adresát není doma, zkusí to příště. Po pátém marném pokusu
dopis odloží stranou a nechá ho na stole pro vedoucího.

```
HTTP požadavek (CancelAsync)                  OutboxWorker (každé 2 s, po celou dobu běhu aplikace)
─────────────────────────────                 ──────────────────────────────────────────────────────
order.Cancel()                                OutboxProcessor.ProcessPendingAsync()
SaveChangesAsync                                1. načte max. 20 zpráv: ProcessedAtUtc IS NULL
  BEGIN                                            a Attempts < 5, od nejstarší
  UPDATE Orders ...                             2. pro každou zprávu nový scope (nový DbContext):
  INSERT OutboxMessages (OrderCancelled)           – OutboxEventReader: JSON → OrderId → načte Order
  COMMIT                                             → new OrderCancelled(order, ...)
odpověď 200 ◄── uživatel nečeká na handlery        – dispatcher → handlery (vrácení skladu, notifikace)
                                                   – úspěch: ProcessedAtUtc = teď, Error = NULL
                                                   – chyba:  Attempts + 1, Error = text chyby
```

**Eventual consistency v praxi:** hned po zrušení objednávky je zboží na skladě ještě odečtené.
Asi do 2 sekund se vrátí. Na stránce Produkty to uvidíte až po obnovení.

Ověřeno v běžící aplikaci:

```
Order 1 created; stock now: 3
Cancelled; stock immediately: 3     ← handler ještě neproběhl
Stock after 3 s: 5                  ← worker zprávu doručil
```

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `Infrastructure/Outbox/OutboxProcessor.cs` | jedno kolo pošťáka: dávka 20 zpráv, scope na zprávu, značka úspěchu / počítání pokusů; `BatchSize`, `MaxAttempts` |
| `Infrastructure/Outbox/OutboxEventReader.cs` | z řádku outboxu znovu sestaví doménovou událost (načte `Order` podle `OrderId`) |
| `Web/BackgroundServices/OutboxWorker.cs` | `BackgroundService` s `PeriodicTimer` (2 s), volá `OutboxProcessor` |
| `tests/.../TestDoubles/InMemoryDatabase.cs` | pojmenovaná sdílená SQLite DB v paměti – každý scope má vlastní připojení jako v aplikaci |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `Infrastructure/Persistence/MiniShopDbContext.cs` | už nerozesílá – jen uloží entity + outbox v jedné transakci; z konstruktoru zmizel dispatcher i `TimeProvider` |
| `Infrastructure/Outbox/OutboxMessage.cs` | nový sloupec `Attempts` (počet neúspěšných pokusů) |
| `Infrastructure/DependencyInjection.cs` | registrace `OutboxEventReader` (scoped) a `OutboxProcessor` (singleton) |
| `Web/Program.cs` | `AddHostedService<OutboxWorker>()` |
| `Application/Services/OrderService.cs` | jen komentáře: handlery běží později přes outbox |
| `Application/Orders/EventHandlers/ReturnStockOnOrderCancelledHandler.cs` | jen komentář: běží ve workeru, opakuje se |
| `tests/.../TestDoubles/RecordingDomainEventDispatcher.cs` | zapisuje do sdíleného seznamu (každá zpráva má vlastní scope) |
| `tests/.../Application/OrderServiceTests.cs` | skládá služby přes `AddInfrastructure`, každá akce ve vlastním scope, po akci `ProcessOutboxAsync()` |
| `tests/.../Infrastructure/OutboxTests.cs` | nové testy procesoru: doručení, opakování, poison message, neexistující objednávka |

## Rozhodnutí v tomto kroku

- **Doručuje se jen přes workera.** Okamžité doručení po commitu z kroku 4 zmizelo. Kdyby zůstalo,
  worker by mohl sebrat zprávu, která se zrovna doručuje, a handlery by proběhly dvakrát.
  Cena je eventual consistency: sklad se po zrušení vrátí se zpožděním.
- **Payload → událost načtením objednávky.** Handlery čekají `OrderCancelled` s celou entitou `Order`.
  `OutboxEventReader` ji načte z DB podle `OrderId`, takže se handlery nemusely měnit.
  Handler ale vidí *aktuální* stav objednávky, ne stav v okamžiku události. Pro současné handlery to nevadí.
- **Nový scope pro každou zprávu.** `OutboxProcessor` je singleton, `DbContext` a handlery jsou scoped.
  Vlastní scope znamená čerstvý DbContext, takže rozpracované změny handleru, který selhal,
  se nepřenesou do zpracování další zprávy.
- **Jedna dávka na kolo.** Zpráva, která selhala, se zkusí znovu nejdřív za 2 sekundy.
  Handler tak nedostane pět pokusů těsně po sobě.
- **Poison message = řádek s `Attempts = 5`.** Samostatnou dead-letter frontu zatím nemáme. Stačí dotaz
  `SELECT * FROM OutboxMessages WHERE ProcessedAtUtc IS NULL AND Attempts >= 5`. Worker o takové
  zprávě zapíše do logu chybu (`LogError`).
- **Worker je ve Webu.** `BackgroundService` patří k hostingu, který má Web díky ASP.NET k dispozici.
  Infrastruktura tak nepotřebuje nový NuGet balíček. Logika je v `OutboxProcessor`, který jde testovat bez hostu.
- **Testy volají procesor ručně.** Žádné čekání ani `Thread.Sleep`: test po akci zavolá `ProcessOutboxAsync()`,
  stejně jako by to udělal worker. Test `PlaceOrder_PredZpracovanimOutboxu_HandleryJesteNeprobehly`
  ukazuje stav mezi tím.

## Známá omezení

- **Opakování spustí všechny handlery události znovu.** Když selže jen vrácení skladu, při opakování
  odejde i notifikace podruhé. A pokud handler uspěje, ale aplikace spadne před označením zprávy,
  proběhne celý znovu. Proti tomu chrání idempotence (inbox) v kroku 6.
- **Jen jedna instance aplikace.** Dvě instance by si mohly vzít stejnou zprávu. Řešením by bylo zamykání
  (např. sloupec „zpracovává“ s časem).
- **Žádný rostoucí odstup mezi pokusy** (exponential backoff). Pět pokusů proběhne zhruba za 10 sekund,
  což na delší výpadek nestačí.
- **Pořadí podle `OccurredAtUtc`.** Dvě události se stejným časem nemají zaručené pořadí.
- **V Development logu se každé 2 s objeví SQL dotaz workera**, protože `appsettings.Development.json`
  vypisuje všechny dotazy EF (`Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command: Information`).
  Když to vadí, přepněte tuto úroveň na `Warning`.

⚠️ Přibyl sloupec `Attempts` – před spuštěním smažte `src/MiniShop.Web/minishop.db`.

## Jak ověřit

1. `dotnet test` – projde všech 37 testů (`OutboxTests` má 8 testů, z toho 5 nových pro procesor).
2. Smažte `minishop.db`, spusťte aplikaci, vytvořte objednávku a zrušte ji.
   Hned po zrušení má produkt na stránce Produkty ještě nižší zásobu. Po obnovení za pár sekund se vrátí.
3. V terminálu uvidíte řádky z `LoggingOrderNotifier` a `[Událost] ...` až chvilku po odpovědi na požadavek.
4. V DB Browseru for SQLite mají řádky `OutboxMessages` vyplněné `ProcessedAtUtc` o něco později než `OccurredAtUtc`.

## Vyzkoušejte sami

Do `ReturnStockOnOrderCancelledHandler.HandleAsync` dejte na první řádek `throw new Exception("test");`,
spusťte aplikaci a zrušte objednávku. V logu uvidíte pět varování „selhala (1/5) … (4/5)“ po dvou sekundách
a nakonec chybu „poison message“. V `OutboxMessages` zůstane řádek `OrderCancelled` s `Attempts = 5`.
Pak řádek s `throw` smažte a v DB nastavte u té zprávy `Attempts = 0` – worker ji do 2 sekund doručí
a zboží se vrátí. Takhle by člověk ručně „opravil“ poison message po vyřešení příčiny.
Všimněte si ale, že notifikace o zrušení odešla šestkrát. To je problém, který řeší krok 6.
