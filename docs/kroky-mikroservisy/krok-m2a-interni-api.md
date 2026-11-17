# Mikroservisy krok M2A: interní API Webu pro Notifications (expand)

Plán: [Mikroservisy-roadmap](../Mikroservisy-roadmap.md). Příprava na vyjmutí modulu Notifications (M2C): data, která
Notifications za běhu potřebuje od Webu, jdou teď dostat i přes HTTP. **Chování produkce se nemění** – Notifications dál
běží ve Webu a používá implementace v procesu; nová cesta se jen ověří (expand → contract).

## Jak to funguje

Notifications zná ostatní moduly jen přes čtyři rozhraní, která implementuje Web (composition root):

| Rozhraní | Endpoint (jen se servisním klíčem) | Odpověď |
| --- | --- | --- |
| `ICustomerContacts` | `GET /internal/customers/{id}/contact` | `CustomerContact` / 404 |
| `IStaffDirectory` | `POST /internal/staff/allowed` `{kind, userIds}` | ID, která oznámení smí dostat |
| `IProductNames` | `GET /internal/products/{id}/name` | `{name}` / 404 |
| `IInvoiceDocuments` | `GET /internal/orders/{id}/invoice`, `…/credit-notes/{číslo}` | PDF / 404 |

- **Dva druhy hostů, dva zámky.** Lidé chodí s cookie, služby se servisním klíčem. Web dostal druhé schéma ověření
  (`ApiKey` ze ServiceDefaults, M1) a politiku `RequireServiceApiKey` jen pro `InternalController`. Přirovnání: služební
  vchod do budovy – zaměstnanecká karta (klíč) otevře jen služební vchod, vstupenka návštěvníka (cookie) ho neotevře
  a karta zase nepustí do sálu.
- **Caddy `/internal/*` z internetu vrací 404** – interní API je jen pro síť Compose, klíč je druhá vrstva.
- **Klient budoucí služby** `WebInternalApiClient` (Infrastructure) implementuje všechna čtyři rozhraní – handlery
  Notifications nepoznají, jestli Web běží vedle nich, nebo ve vedlejším kontejneru. 404 = `null` (jako v procesu),
  jiná chyba = výjimka → zpráva se zopakuje, nejhůř skončí v dead-letter frontě.
- **Test kontraktu** (`InternalApiTests`): klient volá skutečný Web v paměti a výsledek musí být stejný jako
  z implementace v procesu – i role, blokace a PDF. Až M2C přepne Notifications na HTTP, chování se nezmění.

## Nové soubory

- `src/MiniShop.Web/Controllers/InternalController.cs` (+ `StaffAllowedRequest`, `ProductNameResponse`).
- `src/MiniShop.Web/Internal/InternalApi.cs` – schéma `ApiKey` vedle cookie, politika `RequireServiceApiKey`, fail fast bez klíče.
- `src/MiniShop.Infrastructure/Notifications/WebInternalApiClient.cs`.
- `tests/MiniShop.Tests/Web/InternalApiTests.cs`.

## Změněné soubory

- `ServiceApiKey.WebInternalConfigurationKey` (`WebInternalApi:ApiKey`), hláška s jménem secretu pro každý klíč.
- Web `Program.cs` (`AddWebInternalApi`), `appsettings.Development.json`, AppHost (parametr `web-internal-api-key`).
- `deploy/Caddyfile` (`handle /internal/*` → 404), `compose.yaml`, `.env.example`, `deploy.yml` (secret),
  `deploy.sh` – `WEB_INTERNAL_API_KEY` povinný, **dopředu pro M2C** `NOTIFICATIONS_API_KEY` (volitelný) a postupné
  nasazení `notifications-api` před Webem (jen když ji compose commitu má) – M2C tak `deploy.sh` na VM znovu instalovat nemusí.
- `ci.yml` (klíč v `.env` jobů E2E a ZAP), testy (`MiniShopWebFactory.InternalApiKey`, `InvoicePageTests.IssueInvoiceAsync` sdílený).
- Příručka: modul Notifications (závislosti a interní API), HTTPS a přístup, GitHub secrets, historie; `docs/secrets.md` (R18); `CLAUDE.md`.

## Rozhodnutí

- **Endpointy jen volají existující implementace** – pravidla (jen potvrzený e-mail, role při odeslání, blokace) na jednom místě.
- **Dotaz, ne kopie dat** – osobní údaje zůstanou v Accounts a platí v okamžiku odeslání (souhlas odvolaný minutu
  před připomínkou platí). Cena: když Web dlouho neběží, e-mail počká.
- **Web sám sebe po síti nevolá** – v M2A klient nikdo nepoužívá, přepnutí až s vyjmutím služby (M2C).
- **V adrese jen ID** – e-mail a jméno jen v těle odpovědi, ne v trase ani logu (hlídá test).
- **`deploy.sh` připravený na M2C** – jedna instalace na VM místo dvou.

## Známá omezení

- Výpadek Webu po M2C zdrží e-maily, které potřebují kontakt nebo PDF (consumer zkusí 5×, pak dead-letter fronta).
- Kdo zná klíč a dostane se do sítě Dockeru, přečte kontakt zákazníka podle ID a stáhne fakturu – proto vlastní klíč
  jen pro tohle API a zavřené `/internal/*` v Caddy.

## Jak ověřit

1. CI zelené; před sloučením secret `WEB_INTERNAL_API_KEY` a nový `deploy.sh` na VM.
2. Po nasazení z internetu `https://minishop-tichy.cz/internal/products/1/name` → **404** (Caddy).
3. Na VM zevnitř s klíčem (postup v `docs/secrets.md`, R18) → `200 OK` nebo `404`, ne `401`.

## Vyzkoušejte sami

- Lokálně (AppHost) `GET https://localhost:7180/internal/products/1/name` bez hlavičky → 401; s hlavičkou
  `X-Api-Key: vyvojovy-klic-interniho-api-webu-jen-pro-lokalni-beh` → název produktu.
- Přihlášený admin otevře totéž v prohlížeči → 401 (cookie interní API neotevře).
