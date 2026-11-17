# E-shop krok 23: Oznámení push

Zákazník si na telefonu (nebo počítači) zapne oznámení a MiniShop mu krátce dá vědět, že je objednávka potvrzená,
na cestě, doručená, zrušená, že je vyřízená vratka nebo že je zboží z „Dejte mi vědět“ zase skladem. Navazuje na
instalovatelnou aplikaci (vzhled krok D): service worker `sw.js` oznámení ukáže, i když je aplikace zavřená.

Přirovnání: e-mail je dopis do schránky, push je SMS na displej – doručuje ji ale pošta výrobce prohlížeče (Google,
Apple, Mozilla), ne my. MiniShop jí dá zalepenou obálku (obsah šifrovaný klíčem telefonu) s podpisem (VAPID).

## Jak to funguje

1. **Zapnutí** – stránka *E-maily a oznámení* (`/Account/EmailPreferences`, jen zákazník), `wwwroot/js/push.js`:
   souhlas prohlížeče → `pushManager.subscribe(veřejný klíč VAPID)` → prohlížeč vrátí *endpoint* (adresu „schránky“
   zařízení u své push služby) a klíče `p256dh` + `auth` → POST `?handler=PushSubscribe` (JSON, antiforgery token
   v hlavičce) → `PushSubscriptionService` uloží `PushSubscription` (schéma `notifications`, tabulka `PushSubscriptions`).
   Popisek zařízení („Android · Chrome“) z User-Agent, celý řetězec se neukládá. Nejvýš 10 zařízení na zákazníka.
2. **Odeslání** – `OrderPushHandler` v modulu Notifications poslouchá stejné události jako e-maily (`OrderConfirmedV1`,
   `OrderRejectedV1`, `OrderShippedV1`, `OrderCompletedV1`, `OrderCancelledV1`, `ReturnRejectedV1/V2`,
   `ReturnCompletedV1/V2`). Zákazníka zjistí z uloženého příjemce (`OrderRecipient.CustomerId`) a `PushNotifier` pošle
   oznámení na všechna jeho zařízení přes `WebPushSender` (knihovna Lib.Net.Http.WebPush – šifrování `aes128gcm`,
   podpis VAPID, TTL 24 h). „Dejte mi vědět“ posílá push vedle e-mailu v `BackInStockHandler`.
3. **Vlastní handler = vlastní inbox:** když push služba nejde, zopakuje se jen zpráva pro `OrderPushHandler` – e-mail
   neodejde podruhé (inbox si pamatuje `EventId + Handler`). U „Dejte mi vědět“ (stejný handler jako e-mail) se chyba
   jen poznamená na trasu (`minishop.push.failed`).
4. **Úklid:** push služba odpoví 404/410 (oznámení vypnutá, aplikace odinstalovaná) → `PushNotifier` zařízení smaže.
   Smazání účtu (`CustomerAnonymizedV1`) smaže všechna zařízení zákazníka.
5. **Na zařízení** – `sw.js`: událost `push` → `showNotification` (ikona, badge, `tag` – opakované doručení nahradí
   předchozí oznámení), `notificationclick` → otevře nebo přepne na stránku MiniShopu (`/Orders/12`); cizí adresu ne.
6. **Klíče VAPID** – `dotnet run --project src/MiniShop.Web -- generate-vapid-keys`. Veřejný klíč `PUSH_VAPID_PUBLIC_KEY`
   v `deploy/production.env`, soukromý secret `PUSH_VAPID_PRIVATE_KEY` (deploy.yml → deploy.sh → `.env` → kontejner `web`).
   Bez klíčů `DisabledPushSender` – sekce na stránce se neukáže. Jeden klíč nebo špatný tvar = Web se nespustí.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Domain/Notifications/PushSubscription.cs` | zařízení zákazníka (endpoint jen https, klíče, popisek) |
| `src/MiniShop.Application/Notifications/Push.cs` | `PushMessage`, `PushDeliveryResult`, `IPushSender`, `IPushSubscriptionRepository` |
| `src/MiniShop.Application/Notifications/PushSubscriptionService.cs` | zapnout / vypnout / odebrat / seznam |
| `src/MiniShop.Application/Notifications/PushNotifier.cs` | odeslání na všechna zařízení, úklid po 404/410 |
| `src/MiniShop.Application/Notifications/PushTemplates.cs` | texty oznámení (bez osobních údajů) |
| `src/MiniShop.Application/Notifications/OrderPushHandler.cs` | odběratel událostí objednávky a vratky |
| `src/MiniShop.Infrastructure/Notifications/WebPushSender.cs` | `PushSettings`, `WebPushSender`, `DisabledPushSender` |
| `src/MiniShop.Infrastructure/Notifications/PushSubscriptionRepository.cs` + `Configurations/PushSubscriptionConfiguration.cs` | uložení |
| `src/MiniShop.Infrastructure/Notifications/Migrations/…_PushSubscriptions.cs` | nová tabulka (jen přidává – expand) |
| `src/MiniShop.Web/Notifications/GenerateVapidKeysCommand.cs` | příkaz `generate-vapid-keys` |
| `src/MiniShop.Web/Notifications/PushDeviceLabel.cs` | „Android · Chrome“ z User-Agent |
| `src/MiniShop.Web/wwwroot/js/push.js` | zapnutí/vypnutí v prohlížeči |
| `src/MiniShop.Web/wwwroot/icons/badge.svg`, `badge-96.png` | ikonka ve stavovém řádku Androidu |
| `src/MiniShop.ServiceDefaults/PushServiceHosts.cs` | `url.full` push služby v trase jen se jménem serveru |
| `tests/MiniShop.Tests/Application/OrderServiceTests.Push.cs`, `tests/MiniShop.Tests/Web/PushNotificationsTests.cs`, `TestDoubles/RecordingPushSender.cs` | testy |

## Změněné soubory

- `OrderNotificationsHandler` (smazání účtu smaže zařízení), `BackInStockHandler` (push vedle e-mailu),
  `NotificationsDbContext`, `NotificationsInfrastructure`, `ApplicationServiceCollectionExtensions` (`AddOrderPushHandlers`,
  `WebPushSender` jako typed `HttpClient`), `Program.cs` (příkaz), `ServiceDefaultsExtensions` (zkrácení adresy v trase).
- Stránky: `Account/EmailPreferences` (*E-maily a oznámení* – stav, zapnout/vypnout, seznam zařízení, zkušební
  oznámení), `Privacy` (nový osobní údaj, push služba jako zpracovatel). `wwwroot/sw.js` (`push`, `notificationclick`).
- Nasazení: `deploy/compose.yaml`, `deploy/production.env` (`PUSH_VAPID_PUBLIC_KEY`), `deploy/.env.example`,
  `.github/workflows/deploy.yml` + `deploy/deploy.sh` (secret `PUSH_VAPID_PRIVATE_KEY`, kontrola tvaru), `docs/secrets.md` (R16).
- Testy: `OrderServiceTests` (registrace), `MiniShopWebFactory.Push`, `AuthorizationMatrixTests`, `PwaTests`.
- Příručka: modul Notifications (sekce Oznámení push + sekvenční diagram), observabilita (atributy `minishop.push.*`),
  třetí strany (knihovna, push služby), GitHub secrets, vzhled, historie; CLAUDE.md.

## Rozhodnutí

- **Nový odběratel, ne změna Orderingu** – žádný nový kontrakt; `OrderPushHandler` poslouchá existující události.
- **Samostatný handler** místo přidání do `OrderNotificationsHandler` – výpadek push služby nesmí zopakovat e-mail.
- **Bez osobních údajů v oznámení** – vidí ho každý, kdo drží telefon; důvod zamítnutí (volný text admina) jen v e-mailu.
- **Přijetí objednávky bez push** – zákazník je v tu chvíli u stránky, oznámení by bylo zbytečné.
- **Antiforgery i pro JSON** – handler stránky (ne samostatné API), token v hlavičce `RequestVerificationToken`.
- **Zařízení patří přihlášenému** – stejný endpoint z jiného účtu (sdílený tablet) zařízení převezme.
- **Zkušební oznámení** na stránce – ověření bez objednávky.
- **Knihovna místo vlastní kryptografie** – šifrování RFC 8291 a JWT VAPID se ručně nepíšou.

## Známá omezení

- Doručení je *best effort*: telefon offline déle než 24 h oznámení nedostane; e-maily zůstávají hlavní kanál.
- iPhone/iPad jen v aplikaci přidané na plochu (iOS 16.4+).
- Výměna klíčů VAPID = zákazníci si oznámení zapnou znovu.
- Opakované doručení (pád mezi odesláním a inboxem) = oznámení podruhé, displej ho ale díky `tag` nahradí.
- Oznámení pro zaměstnance (nová objednávka ke zabalení) zatím ne.
- Skutečné doručení push službou E2E test neověří – ověřuje se ručně (zkušební oznámení).

## Jak ověřit

- `dotnet test` – `OrderServiceTests.Push_*` (události, 410, opakování jen push, „Dejte mi vědět“, smazání účtu, převzetí
  zařízení, limit), `PushNotificationsTests` (stránka, antiforgery, skutečné šifrování a podpis knihovnou proti podvržené
  push službě, zkrácení adresy v trase), `AuthorizationMatrixTests`, `PwaTests`.
- Produkce: *E-maily a oznámení* → **Zapnout na tomto zařízení** → povolit → **Poslat zkušební oznámení** → oznámení na
  displeji, klepnutí otevře stránku. Pak objednávka → admin Expedice → *Odeslat* → „Objednávka č. … je na cestě“.

## Vyzkoušejte sami

1. Lokálně: `generate-vapid-keys`, klíče do user secrets Webu (`PushNotifications:VapidPublicKey` / `VapidPrivateKey`),
   spusťte AppHost a na `https://localhost:7180/Account/EmailPreferences` zapněte oznámení v Chromu.
2. V DevTools → *Application* → *Service workers* → **Push** pošlete testovací text – ukáže se oznámení „MiniShop“.
3. Vypněte oznámení v nastavení webu (ikona zámku) a pošlete zkušební oznámení – seznam zařízení po odpovědi push služby
   (410) zařízení sám smaže.
