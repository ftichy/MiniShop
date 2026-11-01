# Účty krok 7: Zabezpečení skladu

Služba skladu (`Inventory.Api`) dosud přijala jakýkoli požadavek – chránilo ji jen to, že není vidět z internetu.
Teď chce na celém `/api` **servisní klíč**, který zná jen Web. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: do skladu nestačí projít branou areálu (síť Dockeru) – u dveří je ještě zámek a klíč má jen obchod.

```
 Web ── InventoryApiClient ── InventoryApiKeyHandler ──(X-Api-Key: …)──► Inventory.Api
                                                                          UseAuthentication: ApiKeyAuthenticationHandler
                                                                            bez hlavičky → NoResult, špatný klíč → Fail
                                                                          UseAuthorization: fallback policy = ověřený
                                                                            /api/* bez klíče → 401
                                                                            /health, /alive → AllowAnonymous

 klíč InventoryApi:ApiKey   Aspire: vygenerovaný parametr "inventory-api-key" (user secrets AppHostu)
                            bez Aspire: appsettings.Development.json (obě služby, vývojový klíč)
                            Compose: INVENTORY_API_KEY v .env (povinný, :?)
                            produkce: GitHub secret → deploy.yml → deploy.sh (64 hex) → .env → oba kontejnery
 chybí / < 32 znaků → služba se nespustí (ServiceApiKey.Read – fail fast)
```

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.ServiceDefaults/ServiceApiKey.cs` | Jméno hlavičky, klíč konfigurace, načtení klíče s kontrolou (sdílí Web i sklad) |
| `src/MiniShop.Inventory.Api/ApiKeyAuthentication.cs` | Autentizační schéma „ApiKey“ – porovnání v konstantním čase |
| `src/MiniShop.Inventory.Api/appsettings.Development.json` | Vývojový klíč pro běh bez Aspire (do image se nedostane – `.dockerignore`) |
| `src/MiniShop.Web/Inventory/InventoryApiKeyHandler.cs` | DelegatingHandler – přidá hlavičku ke každému požadavku na sklad |
| `tests/MiniShop.Tests/InventoryApi/InventoryApiAuthenticationTests.cs` | Celé Inventory.Api v paměti: bez klíče / špatný klíč → 401 (všechny endpointy), správný → 200, health bez klíče |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Inventory.Api/Program.cs` | Autentizace, fallback policy, `UseAuthentication/UseAuthorization`, OpenAPI ve vývoji bez klíče |
| `src/MiniShop.Web/Extensions/ApplicationServiceCollectionExtensions.cs` | `AddInventoryApiClient(configuration)` + `InventoryApiKeyHandler` |
| `src/MiniShop.Web/appsettings.Development.json` | Vývojový klíč |
| `src/MiniShop.AppHost/AppHost.cs` | Parametr `inventory-api-key` (generovaný, tajný, uložený) pro obě služby |
| `deploy/compose.yaml`, `deploy/.env.example` | `InventoryApi__ApiKey: ${INVENTORY_API_KEY:?…}` u `web` i `inventory-api` |
| `deploy/deploy.sh`, `.github/workflows/deploy.yml` | Nový povinný secret `INVENTORY_API_KEY` (64 hex znaků) |
| `docs/secrets.md` | Řádek `INVENTORY_API_KEY` + postup výměny R9 |
| `tests/…/MiniShopWebFactory.cs`, `InventoryApiClientTests.cs` | Klíč pro Web v testech; hlavička v požadavku, chybějící/krátký klíč |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | Inventory (zero trust), secrets, historie; JWT odloženo |

## Rozhodnutí v tomto kroku

- **Servisní klíč, ne JWT** – pro dvě služby nasazované spolu stačí; JWT (sklad by sám ověřil roli Admin) je odložené.
- **Klíč i pro čtení** – stejně jako fallback policy ve Webu: co není výslovně otevřené, je zamčené. Jediný čtenář je Web.
- **Health checky bez klíče** – Docker healthcheck, Caddy (`/health/inventory`) a UptimeRobot klíč nemají.
- **Fail fast** – bez klíče se služba nespustí (a nasazení se vrátí na předchozí verzi), místo aby běžela odemčená
  nebo tiše vracela 401.
- **Konstantní čas porovnání** (`CryptographicOperations.FixedTimeEquals`) a **klíč nikdy do logu**.
- **64 hex znaků** (256 bitů) – jen znaky, které nerozbijí `.env` ani YAML; `deploy.sh` jiný tvar odmítne.
- **Aspire klíč generuje a pamatuje** (`persist: true`) – lokálně není co nastavovat.

## Známá omezení

- Jeden sdílený klíč – kdo ho zná, je pro sklad „Web“. Sklad neví, který uživatel akci spustil (audit je ve Webu).
- Výměna klíče = restart obou kontejnerů; pár sekund může stránka Produkty hlásit „sklad nedostupný“.
- Klíč je v proměnných prostředí kontejnerů (vidí ho `docker inspect` na VM) – stejně jako ostatní secrets.
- Lokální `deploy/.env` (Compose na PC) musí klíč doplnit, jinak `docker compose up` skončí chybou.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V Aspire dashboardu je parametr **inventory-api-key** (hodnota skrytá). Stránka Produkty funguje.
2. Zkuste sklad bez klíče – adresu `inventory-api` najdete v dashboardu:
   `curl -i https://localhost:7181/api/products` → `401 Unauthorized`; `curl -i https://localhost:7181/health` → `200`.
3. Se špatným klíčem `-H "X-Api-Key: spatny"` → 401 a v logu skladu „Požadavek na sklad se špatným servisním klíčem“.

### Produkce (ručně, po krocích – PŘED sloučením PR)

1. Vygenerovat klíč (64 hex znaků) – postup v `docs/secrets.md` R9.
2. GitHub → *Environments → production* → secret `INVENTORY_API_KEY`.
3. Nový `deploy.sh` na VM (nový `deploy.yml` posílá řádek `INVENTORY_API_KEY=` – starý skript by ho odmítl):
   ```text
   curl -fsSL https://raw.githubusercontent.com/ftichy/MiniShop/<sha-větve>/deploy/deploy.sh -o /tmp/minishop-deploy
   sudo install -m 755 -o root -g root /tmp/minishop-deploy /usr/local/bin/minishop-deploy
   ```
4. Sloučit PR → nasazení → stránka Produkty funguje; na VM
   `sudo -u deploy docker compose exec web bash -c 'exec 3<>/dev/tcp/inventory-api/8080; printf "GET /api/products HTTP/1.0\r\n\r\n" >&3; head -1 <&3'`
   → `HTTP/1.1 401 Unauthorized` (z kontejneru Webu bez klíče).

## Vyzkoušejte sami

- Zakomentujte `.AddHttpMessageHandler(…)` v `AddInventoryApiClient` a spusťte AppHost. Co ukáže stránka Produkty a proč?
- Smažte `InventoryApi` z `appsettings.Development.json` skladu a spusťte ho bez Aspire. Co napíše log?
- Proč by obyčejné `==` při porovnání klíče bylo (teoreticky) slabší než `FixedTimeEquals`?
