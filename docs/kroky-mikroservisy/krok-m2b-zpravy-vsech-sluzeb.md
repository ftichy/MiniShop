# Mikroservisy krok M2B: zprávy ze všech služeb

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Původně součást přesunu Notifications – vyčleněno jako
samostatný krok **před** ním: dead-letter zprávy e-mailů musí jít vrátit z `/Admin/Messages` dřív, než Notifications
odejde z Webu (přehled tržeb jde přestavět, odeslaný e-mail ne).

## Jak to funguje

```
/Admin/Messages ──► IServiceMessages (pro každou službu)
                      ├─ web            LocalServiceMessages ──► MessagesOverview (v procesu)
                      ├─ inventory-api  RemoteServiceMessages ── GET/POST /api/messages (X-Api-Key skladu) ──► MessagesOverview
                      └─ reporting-api  RemoteServiceMessages ── GET/POST /api/messages (X-Api-Key přehledu) ──► MessagesOverview
```

- **Logika jednou:** `MessagesOverview` (sdílené jádro) = stav outboxů modulů služby (čekající, poison), dead-letter
  fronty, „Zkusit znovu“, „Vrátit do fronty“. Dřív to měla jen stránka Webu; teď ho používá Web i každá služba.
- **Každá služba** má malý `MessagesController` (`GET /api/messages`, `POST …/{modul}/{id}/retry`,
  `POST …/queues/{fronta}/requeue`) za svým servisním klíčem. Cizí fronta = 400 (sklad nevrátí zprávy Webu).
- **Stránka** se ptá všech služeb souběžně; každá má vlastní oddíl. Nedostupná služba = upozornění jen u ní
  (přirovnání: vrátnice obvolá všechna oddělení – když jedno nebere telefon, ostatní hlásí normálně).
- Formuláře nesou i jméno služby (`service`), takže tlačítko trefí správnou službu.

## Nové soubory

- `src/MiniShop.Infrastructure/Hosting/MessagesOverview.cs` (+ `ServiceMessagesStatus`, `MessageRetryResult`, `MessageRequeueResult`).
- `src/MiniShop.Inventory.Api/Controllers/MessagesController.cs`, `src/MiniShop.Reporting.Api/Controllers/MessagesController.cs`.
- `src/MiniShop.Web/Messages/ServiceMessages.cs` – `IServiceMessages`, `LocalServiceMessages`, `RemoteServiceMessages`,
  `ServiceMessagesRegistration` (adresy a klíče jako `InventoryApiClient`/`ReportingApiClient`).
- Testy: `Web/MessagesPageTests.cs`, `Web/RemoteServiceMessagesTests.cs`, `TestDoubles/FakeServiceMessages.cs`.

## Změněné soubory

- Stránka `Admin/Messages` (model i Razor) – oddíl pro každou službu, `service` ve formulářích.
- `DependencyInjection.AddInfrastructureCore` (registrace `MessagesOverview`), Web `AddApplication` (`AddServiceMessages`).
- Testy: `InventoryApiAuthenticationTests` a `ReportingApiTests` (`/api/messages` – klíč, jen vlastní modul, cizí fronta 400),
  `MiniShopWebFactory` (podvržené služby).
- Příručka: provoz zpráv, modul Reporting, historie; roadmapa (přečíslováno: Notifications je teď **M2C**), `CLAUDE.md`.

## Rozhodnutí

- **Controller v každé službě (kopie ~20 řádků), logika ve sdíleném jádru** – Infrastructure nezná ASP.NET MVC; tři
  stejné tenké controllery jsou čitelnější než nový sdílený projekt.
- **Stejné servisní klíče jako ostatní API služby** – žádný nový secret, žádné ruční kroky.
- **Souběžné dotazy, chyba jen u jedné služby** – výpadek skladu nesmí schovat zprávy Webu.
- `deploy.sh` (nainstalovaný na VM v M2A) ve svých komentářích říká přesunu Notifications ještě „M2B“ – kvůli komentáři
  se na VM nepřeinstalovává, opraví se při nejbližší skutečné změně skriptu.

## Známá omezení

- Obsah odložené zprávy dál ukáže jen RabbitMQ Management (stránka ukazuje počty a chybu u poison zpráv outboxu).
- Pomalá nedostupná služba zdrží načtení stránky o dobu timeoutu HTTP klienta (resilience ze ServiceDefaults).

## Jak ověřit

1. CI zelené, nasazeno (bez ručních kroků).
2. *Správa → Zprávy*: tři oddíly – Web, Sklad (inventory-api), Přehled tržeb (reporting-api), u každého outboxy
   a dead-letter fronty.

## Vyzkoušejte sami

- Na VM `docker compose stop reporting-api` → na stránce Zprávy jen u přehledu tržeb upozornění, Web a sklad normálně;
  `docker compose start reporting-api`.
