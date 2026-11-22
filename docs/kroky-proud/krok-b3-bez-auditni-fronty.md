# Proud krok B3: Bez auditní fronty

Plán: [Proud-roadmap](../Proud-roadmap.md). Auditní fronta `minishop.audit` z EDA kroku 8 („odposlech“ – kopie všech
zpráv do logu) je s proudem `minishop.events` zbytečná. Navíc **zapisovala do logu celý obsah zpráv** – u `OrderPlacedV2`
jméno, e-mail a adresu zákazníka – a logy jdou do souborů na VM (7 dní) i přes Alloy do Grafana Cloud (filtr `privacy`
maže jen IP, user agent a `url.query`). Krok B3 posluchače odstranil a frontu bezpečně smaže.

## Jak to funguje

```
dřív:  exchange každého kontraktu ──► minishop.audit ──► Web RabbitMqAuditConsumer ──► log (celý JSON zprávy)
teď:   exchange každého kontraktu ──► minishop.events (proud B1) ──► stránka Proud zpráv (osobní údaje maskované)

EventStreamSetupWorker (vedoucí kopie Webu): založí proud → RabbitMqObsoleteQueues.DeleteUnusedAsync
   minishop.audit, minishop.audit.dead: QueueDelete(ifUnused: true)
      někdo poslouchá (stará kopie při postupném nasazení) → 406 PRECONDITION_FAILED → za minutu znovu
      nikdo neposlouchá / fronta neexistuje → hotovo, worker skončí
```

Proč nejde frontu jen přestat poslouchat: je trvalá a navázaná na všechny exchange – bez posluchače by se do ní dál
kopírovala každá zpráva a rostla by donekonečna. Proč ne smazat hned: stará kopie Webu, která ji při postupném nasazení
ještě poslouchá, by si ji při obnově spojení založila znovu.

## Nové soubory

- `src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqObsoleteQueues.cs` – seznam zrušených front, mazání s *ifUnused*
  (každý pokus na vlastním kanálu – odmítnutí kanál zavře).
- `tests/MiniShop.Tests/Infrastructure/ObsoleteQueuesTests.cs` – skutečný RabbitMQ: s posluchačem zůstane, bez něj zmizí
  i se zprávou, neexistující fronta nevadí.
- `tests/MiniShop.Tests/Architecture/MessageLoggingConventionTests.cs` – šablona logu s `{Payload}` v `src` = chyba.

## Změněné soubory

- Smazán `RabbitMqAuditConsumer`; `RabbitMqOptions.AuditQueue`, `RabbitMqConsumers` (jen fronty modulů), `DependencyInjection`.
- `appsettings.json` všech čtyř služeb – bez `RabbitMq:AuditQueue`.
- `EventStreamSetupWorker` – po založení proudu maže zrušené fronty.
- `InProcessIntegrationBus` (jen testy) – log bez obsahu zprávy.
- Komentáře `Program.cs` Webu, `RabbitMqIntegrationBus`, `RabbitMqConsumersWorker`.
- Testy: `RabbitMqTests.Consumery_JenFrontyModuluSluzby`.
- Příručka: `eda/broker.md` (diagram s proudem, poučení), `eda/proud-zprav.md`, `slovnicek.md` (wire tap),
  `nasazeni/grafana-tutorial.md` (auditní spany jen ve starších trasách).

## Rozhodnutí

- **Jedno nasazení, mazání s *ifUnused*** místo dvou nasazení (nejdřív přestat poslouchat, pak smazat) – RabbitMQ sám
  pozná, jestli frontu ještě někdo poslouchá. Zprávy ve frontě se zahodí záměrně (*ifEmpty: false*).
- **Úklid ve `EventStreamSetupWorker`** (vedoucí kopie Webu) – proud je náhrada, logicky patří k sobě. Kód úklidu může
  zůstat i do budoucna: neexistující fronta = jedno volání po startu.
- **Pravidlo „obsah zprávy nikdy do logu“** jako test architektury – opakovat se to nesmí.
- `LoggingEmailSender` (bez SMTP – testy, lokální Compose) loguje text e-mailu jen na úrovni Debug – ponecháno.

## Známá omezení

- Co už v logech je, zmizí s retencí (soubory na VM 7 dní, Grafana Cloud podle retence logů free tieru).
- Starší trasy v Grafaně mají dál krátké spany `process … minishop.audit`.

## Jak ověřit

- `dotnet test` – `ObsoleteQueuesTests` (Docker), `MessageLoggingConventionTests`, `RabbitMqTests`.
- Po nasazení: v logu Webu řádek „Zrušené fronty … nejsou v RabbitMQ“ (případně nejdřív „ještě někdo poslouchá“);
  v RabbitMQ (`rabbitmqctl list_queues`) chybí `minishop.audit`; v logu už nejsou řádky „[Přijato z minishop.audit]“.

## Vyzkoušejte sami

- Lokálně spusťte AppHost se starší verzí (fronta vznikne), pak s novou – v RabbitMQ UI fronta `minishop.audit` zmizí.
- Zkuste do některého handleru dopsat `logger.LogInformation("… {Payload}", …)` – `MessageLoggingConventionTests` spadne.
