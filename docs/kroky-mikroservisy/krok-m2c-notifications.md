# Mikroservisy krok M2C: Notifications jako samostatná služba

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Druhý modul vyjmutý z Webu – tentokrát odběratel, který za
běhu potřebuje data jiných modulů. Navazuje na M2A (interní API Webu) a M2B (zprávy všech služeb na `/Admin/Messages`).

## Jak to funguje

```
RabbitMQ ── minishop.notifications ──► notifications-api ──► SMTP (e-maily), push služby (VAPID)
                                           │   ▲
            GET /internal/… (klíč Webu)    │   │  /api/push, /api/staff-alerts, /api/back-in-stock (klíč služby)
                                           ▼   │
                                         Web (účty, kopie katalogu, faktury, stránky)
```

- **Služba `MiniShop.Notifications.Api`** má všechny handlery modulu (e-maily o objednávkách, faktury a dobropisy,
  připomínka košíku, „Dejte mi vědět“, push zákazníkům, oznámení k práci), SMTP a soukromý klíč VAPID. Migrace schématu
  `notifications` spouští ona.
- **Data jiných modulů** (kontakt a souhlas zákazníka, role zaměstnanců, název produktu, PDF faktur) bere z interního
  API Webu přes `WebInternalApiClient` (M2A) – handlery se nezměnily, jen implementace rozhraní je teď HTTP.
- **Stránky Webu** (*E-maily a oznámení*, „Dejte mi vědět“) volají API služby přes `NotificationsApiClient`. Kdo je
  přihlášený a jakou má roli, rozhoduje Web; službě pošle ID účtu (u oznámení k práci i povolené typy). Web zná jen
  **veřejný** klíč VAPID (pro `push.js`), soukromý má jen služba.
- **Výpadky:** služba neběží → stránky ukážou „služba oznámení neodpovídá“, nákup jde dál a e-maily počkají ve frontě.
  Web neběží → e-mail, který potřebuje kontakt nebo PDF, consumer zopakuje 5× a pak odloží do
  `minishop.notifications.dead` → *Správa → Zprávy → Vrátit do fronty* (M2B).
- **Přechod bez odstávky** jako u M1: `deploy.sh` (připravený už v M2A) spustí `notifications-api` před Webem; stará
  kopie Webu a služba chvíli sdílí frontu, společný inbox ve schématu `notifications` zabrání dvojímu e-mailu.

Přirovnání: podatelna (Notifications) se odstěhovala do vedlejší budovy. Dopisy jí nosí pošta (RabbitMQ) jako dřív;
když potřebuje adresu nebo fakturu, zavolá na úřad (interní API Webu). A úřad jí volá, když si zákazník u přepážky
změní nastavení oznámení.

## Nové soubory

- `src/MiniShop.Notifications.Api/` – `Program.cs`, `NotificationsModule.cs` (registrace přesunutá z Webu + `AddWebInternalApiClient`),
  `Controllers/` (`PushController`, `StaffAlertsController`, `BackInStockController`, `MessagesController`), `appsettings*.json`,
  `Properties/launchSettings.json` (port 7183), `Dockerfile`.
- `src/MiniShop.Web/Notifications/NotificationsApiClient.cs` – `INotificationsApi`, `NotificationsApiClient`,
  `NotificationsUnavailableException`, `PushPublicKey`, `AddNotificationsApiClient`.
- Testy: `NotificationsApi/NotificationsApiFactory.cs`, `NotificationsApi/NotificationsApiTests.cs`, `Web/NotificationsOutageTests.cs`.

## Změněné soubory

- Web: `AddNotificationsBridge` místo `AddNotificationsModule` (zůstaly jen implementace pro interní API), bez
  `AddNotificationsInfrastructure` a migrace `notifications`; stránky `Account/EmailPreferences` a `Shop/Detail` přes
  `INotificationsApi` + upozornění při výpadku; `/Admin/Messages` i se službou Notifications; `appsettings.Development.json`.
- `ServiceApiKey.NotificationsConfigurationKey`; AppHost (služba `notifications-api`, Web a služba se odkazují navzájem).
- Nasazení: `compose.yaml` (služba `notifications-api`, volume `notifications-data`; Web jen veřejný klíč VAPID +
  adresa a klíč služby), `compose.grafana.yaml`, `compose.e2e.yaml` (Mailpit i pro službu), `.env.example`,
  `deploy.yml` (secret `NOTIFICATIONS_API_KEY`), `ci.yml` (čtvrtý image, build v E2E a ZAP). `deploy.sh` beze změny.
- Testy: `MiniShopWebFactory` (Web volá `NotificationsApiFactory` přes HTTP, `NotificationsAvailable`, `web.Push` ze
  služby), `PushNotificationsTests` (databáze služby), `OrderServiceTests` (registrace z `NotificationsModule`),
  `ModuleBoundariesTests`, `WorkerRegistrationTests`.
- Příručka: modul Notifications, služby a kontejnery, architektura, lokální spuštění, kontejnery, CI, CD, více instancí,
  secrets, historie; `docs/secrets.md` (R19, SMTP a VAPID u nové služby); roadmapa; `CLAUDE.md`.

## Rozhodnutí

- **Testy Webu proti skutečné službě v paměti přes HTTP** (ne podvrh) – stávající testy stránek zůstaly skoro beze
  změny a ověřují celou cestu stránka → API → služba.
- **Soukromý klíč VAPID jen ve službě** – Web podepisovat nepotřebuje.
- **SMTP v obou** – Web dál posílá e-maily účtu (potvrzení, reset hesla), služba e-maily objednávek.
- **ID účtu v adrese, endpoint zařízení jen v těle** – osobní údaje ne do trasy ani logu.

## Známá omezení

- Výpadek Webu zdrží e-maily s kontaktem nebo PDF (dead-letter po 5 pokusech, ruční vrácení).
- Web a služba se volají navzájem – v AppHostu proto bez `WaitFor` mezi nimi.

## Jak ověřit

1. Před sloučením secret `NOTIFICATIONS_API_KEY` (64 hex znaků, vlastní). `deploy.sh` na VM se neinstaluje.
2. Po nasazení: `docker compose ps` – `notifications-api` healthy; frontu `minishop.notifications` poslouchá jen služba.
3. Objednávka → e-mail o přijetí a potvrzení, oznámení push na telefon; skladník dostane oznámení k práci.
4. *E-maily a oznámení* – seznam zařízení, *Poslat zkušební oznámení*.
5. *Správa → Zprávy* – oddíl Notifications.

## Vyzkoušejte sami

- Na VM `docker compose stop notifications-api` → objednávka projde, stránka *E-maily a oznámení* ukáže upozornění;
  `docker compose start notifications-api` → e-maily z fronty odejdou.
- V Grafaně trasa objednávky: e-mail je span ve službě `notifications-api`, volání `/internal/…` vede zpět do `web`.
