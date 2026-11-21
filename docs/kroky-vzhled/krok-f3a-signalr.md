# Vzhled krok F3a: Živé aktualizace přes SignalR

Živé výřezy z kroku F2 se už nemusí ptát serveru každé 3 s. Když se objednávka změní, server otevřeným stránkám pošle
**signál „obnov se“** přes SignalR (WebSocket) a výřez se obnoví hned. Dotazování zůstává jen jako pojistka.
Podrobně (s diagramem): příručka [Živé aktualizace](../prirucka/eda/zive-aktualizace.md).

## Jak to funguje

```
Order.ChangeStatus → doménová OrderStatusChanged → překladač → OrderStatusChangedV1 (fanout exchange)
   RabbitMQ: kromě front modulů i dočasná fronta KAŽDÉ kopie Webu (minishop.live.<kontejner>.<náhoda>, exclusive, autoDelete)
   každá kopie: RabbitMqBroadcastConsumer → LiveUpdatesNotifier → IHubContext → skupiny order:42, customer:<ID>, staff:…
prohlížeč: live-region.js → /hubs/live (WebSocket, bez vyjednávání) → Subscribe(téma) – hub ověří oprávnění
   changed("order:42") → htmx:trigger live-refresh → výřez z F2 se stáhne a vymění
pojistka: se spojením 1× za 30 s, bez spojení / bez práva každé 3 s (jako F2)
```

- **Proč rozeslání:** dvě kopie Webu, prohlížeč je připojený k jedné. Běžná fronta dá zprávu jen jedné kopii – dočasná
  fronta na kopii ji dá každé. Doručení **nejvýš jednou** (bez inboxu, opakování, dead-letter) – signál je jen nápověda.
- **Témata:** `order:{id}` (detail – vlastník nebo zaměstnanec, který objednávku vidí), `my-orders` (seznam zákazníka,
  skupina podle ID účtu z přihlášení), `staff:fulfilment`, `staff:returns`, `staff:orders` (politiky oblastí).
- Platba (`/Pay`) téma nemá – zůstává dotazování 3 s (krátká stránka, založení platby událost nemá).

## Nové soubory

- `src/MiniShop.Contracts/Orders/OrderStatusChangedV1.cs`, `src/MiniShop.Domain/Orders/Events/OrderStatusChanged.cs`.
- `src/MiniShop.Infrastructure/Integration/RabbitMq/RabbitMqBroadcastConsumer.cs` (+ `IBroadcastMessageHandler`, `BroadcastSubscription`),
  `src/MiniShop.Infrastructure/Hosting/RabbitMqBroadcastWorker.cs` (+ `AddBroadcastConsumer<T>`).
- `src/MiniShop.Web/LiveUpdates/LiveUpdatesHub.cs` (+ `LiveTopics`, `LiveTelemetry`), `LiveUpdatesNotifier.cs`.
- `src/MiniShop.Web/wwwroot/lib/signalr/signalr.min.js` (@microsoft/signalr 8.0.7, MIT) + `LICENSE.txt`.
- `tests/MiniShop.Tests/Web/LiveUpdatesHubTests.cs`; příručka `eda/zive-aktualizace.md`.

## Změněné soubory

- `Order.ChangeStatus` vyvolá `OrderStatusChanged`; `OrderingDomainEvents`, `OrderingDomainEventReader`, překladač, registrace.
- `RabbitMqQueueConsumer` – virtuální `DeclareQueueAsync` a `TelemetryQueueName` (rozeslání: dočasná fronta, metriky bez náhodné části).
- `Program.cs` – `AddSignalR`, `AddBroadcastConsumer<LiveUpdatesNotifier>`, `MapHub` `/hubs/live`; `/hubs/…` bez přihlášení = 401 (ne přesměrování).
- `Ui/LiveRegionTagHelper.cs` – `hx-trigger="live-refresh"`, atribut `live-topic` → `data-live-topic`.
- `wwwroot/js/live-region.js` – časování (signál / 30 s / 3 s), připojení a odběr témat, `window.minishopLive`.
- Stránky: detail, seznam objednávek, Expedice, Vratky – `live-topic`; `_Layout` – `signalr.min.js`.
- Testy: domény (filtr obecné události + nový test), outboxu, `LiveRegionTests`, `FulfilmentPageTests`, `AuthorizationMatrixTests`
  (`/hubs/live/negotiate`), `WorkerRegistrationTests` (Web má rozesílací worker ve všech kopiích), `MiniShopWebFactory.LoginAsync`
  s `CookieContainer`; E2E `PayByCardAsync` čeká na `window.minishopLive.connected`.
- Příručka: `eda/index.md`, `eda/observabilita.md` (`minishop_live_connections`), `architektura/vzhled.md`, `treti-strany/knihovny.md`.

## Rozhodnutí

- **Obecný kontrakt `OrderStatusChangedV1`** z jediného místa (`ChangeStatus`) místo poslouchání desítky událostí –
  pokryje i přechody bez vlastní události.
- **Nejvýš jednou** – ztracený signál dožene pojistka do 30 s; stav je v databázi.
- **Dočasná fronta na kopii v RabbitMQ**, ne Redis backplane – bez nové infrastruktury.
- **Jen WebSocket bez vyjednávání** – u dvou kopií za Caddy by vyjednávání a spojení mohly trefit různé kopie;
  místo long pollingu nastoupí dotazování z F2.
- **Signál bez dat** – stránka si výřez stáhne se svými oprávněními.

## Známá omezení

- Průběh zásilky (kroky dopravce bez změny stavu objednávky) se zatím obnoví jen pojistkou do 30 s – F3b přidá `ShipmentStatusChangedV1`.
- Testy hubu jdou přes long polling (TestServer WebSockety přes HttpClient neumí); WebSocket ověří E2E v CI.
- Platba se dál dotazuje každé 3 s.

## Jak ověřit

- `dotnet test` – `LiveUpdatesHubTests` (oprávnění odběru, doručení signálu), `LiveRegionTests`, `OrderDomainEventsTests`.
- E2E v CI: detail objednávky čekající na platbu se připojí přes WebSocket (`window.minishopLive.connected`).
- Po nasazení: DevTools → Network → WS `/hubs/live` (zprávy `changed`); po potvrzení objednávky žádné dotazy každé 3 s.

## Vyzkoušejte sami

1. Otevřete detail objednávky čekající na platbu; v konzoli prohlížeče `window.minishopLive.connected` → `true`.
2. Zaplaťte v jiné záložce – detail se změní na „Potvrzená“ hned po potvrzení ságou (ne až za 3 s).
3. RabbitMQ UI (`docker exec … rabbitmqctl list_queues name exclusive`) – fronty `minishop.live.…` pro každou kopii Webu.
