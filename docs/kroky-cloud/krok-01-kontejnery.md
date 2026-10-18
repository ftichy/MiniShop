# Cloud krok 1: Kontejnerizace a produkční konfigurace

Dosud MiniShop spouštěl Aspire: vytvořil procesy, předal jim adresy RabbitMQ a skladu a data ležela v SQLite
souborech vedle projektu. V cloudu Aspire neběží. Teď má každá služba vlastní **image** – zabalenou aplikaci
i s runtimem .NET – která běží kdekoli, kde je Docker. Nastavuje se jen **proměnnými prostředí** a trvalá data
ukládá do složky `/data`, na kterou se připojí **volume**. Aspire, `dotnet run` i testy fungují beze změny.

## Jak to funguje

Přirovnání: image je spotřebič v krabici i s návodem. Kontejner je zapojený spotřebič – když ho vyhodíte
a zapojíte nový ze stejné krabice, funguje stejně, ale co bylo uvnitř, je pryč. Proto jídlo (databáze, klíče,
logy) držíme v lednici vedle (**volume**), ne uvnitř spotřebiče.

**Dvoufázový Dockerfile (multi-stage):**

```
┌─ fáze build (mcr.microsoft.com/dotnet/sdk:10.0, ~900 MB) ───────────────┐
│ 1. COPY *.csproj  → dotnet restore      (vrstva v cache, dokud se       │
│                                          nezmění žádný .csproj)         │
│ 2. COPY src/      → dotnet publish -a $TARGETARCH  → /app/publish       │
└─────────────────────────────────────────────────────────────────┬──────┘
                                                                   │ jen výsledek
┌─ fáze final (mcr.microsoft.com/dotnet/aspnet:10.0) ──────────────▼──────┐
│ mkdir /data, chown app → USER app (ne root)                             │
│ ENV výchozí cesty do /data, port 8080                                   │
│ ENTRYPOINT dotnet MiniShop.Web.dll                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

- **Cache vrstev:** každý příkaz v Dockerfile je vrstva. Když se změní jen kód, Docker použije uloženou vrstvu
  s balíčky NuGet a restore přeskočí. Proto se kopírují nejdřív jen `.csproj`.
- **`--platform=$BUILDPLATFORM` a `-a $TARGETARCH`:** SDK běží na architektuře stroje, který staví,
  a jen přeloží výstup pro cílovou architekturu. V kroku 7 tak postavíme image pro ARM VM v Oracle Cloud
  i na x86 runneru GitHubu bez pomalé emulace.
- **Neprivilegovaný uživatel:** .NET image nabízí uživatele `app` (UID 1654). Když by útočník aplikaci
  ovládl, nemá v kontejneru práva roota. Uživatel `app` ale smí zapisovat jen tam, kam mu to dovolíme –
  proto `RUN mkdir -p /data && chown $APP_UID /data` ještě jako root a teprve pak `USER $APP_UID`.

**Konfigurace z proměnných prostředí (12-factor):** stejná image běží na PC i v cloudu, liší se jen proměnné.
ASP.NET Core je čte samo a `__` v názvu znamená `:` v konfiguraci:

| Proměnná | Kdo ji nastavuje | K čemu |
| --- | --- | --- |
| `ConnectionStrings__Ordering` / `__Journeys` / `__Inventory` | Dockerfile (výchozí) | SQLite v `/data/…db` |
| `DataProtection__KeysPath` | Dockerfile Webu | klíče formulářů v `/data/keys` |
| `Serilog__WriteTo__1__Args__path` | Dockerfile | souborový log (2. cíl Serilogu) do `/data/logs` |
| `ASPNETCORE_HTTP_PORTS` | Dockerfile (8080) | port uvnitř kontejneru |
| `ConnectionStrings__messaging` | při spuštění (krok 2: Compose) | adresa RabbitMQ – dosud ji dával Aspire |
| `Services__inventory-api__http__0` | při spuštění, jen Web | adresa skladu – dosud ji dával Aspire |

Hodnoty z Dockerfile jsou jen výchozí, `docker run -e …` nebo Compose je přepíše.

**Data Protection klíče:** ASP.NET jimi podepisuje antiforgery token ve formuláři objednávky. Bez nastavení
je v kontejneru drží jen v paměti, takže by po každém restartu (= po každém nasazení) vznikly nové a formulář
otevřený před nasazením by po odeslání skončil chybou 400. Když je vyplněné `DataProtection:KeysPath`,
`Program.cs` Webu klíče ukládá do souboru. Ověřeno: po smazání a novém spuštění kontejneru zůstal stejný klíč.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `src/MiniShop.Web/Dockerfile` | dvoufázový build Webu, uživatel `app`, výchozí cesty do `/data`, port 8080 |
| `src/MiniShop.Inventory.Api/Dockerfile` | totéž pro sklad (bez Data Protection) |
| `.dockerignore` | do build kontextu nejdou `bin/`, `obj/`, `*.db`, `logs/`, `appsettings.Development.json`, `.git`, testy, AppHost |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Web/Program.cs` | `AddDataProtection().PersistKeysToFileSystem(…)`, když je nastavené `DataProtection:KeysPath` |
| `src/MiniShop.Web/appsettings.json` → `appsettings.Development.json` | sekce `Services:inventory-api` (`https://localhost:7181`) přesunuta do Development |

## Rozhodnutí v tomto kroku

- **Dockerfile místo `dotnet publish /t:PublishContainer`.** SDK umí image postavit bez Dockerfile, ale nedá se
  v něm vytvořit složka `/data` s právy pro uživatele `app` (zbývalo by běžet jako root). Dockerfile je navíc
  standard, který potkáte u každého jazyka, a multi-arch build přes Buildx (krok 7) s ním funguje přímo.
- **Adresa skladu jen v `appsettings.Development.json`.** Service discovery dává přednost `https` adrese.
  Když byla v `appsettings.json`, platila i v kontejneru a přebila `http` adresu z proměnné prostředí
  (Web volal `localhost:7181` uvnitř kontejneru). Lokální adresa patří k vývoji; mimo Development ji musí
  dodat Aspire nebo Compose. `dotnet run` bez Aspire běží v Development (launchSettings), takže funguje dál.
- **Výchozí cesty v image, ne v `appsettings.Production.json`.** Cesty jako `/data/ordering.db` dávají smysl
  jen v kontejneru, ne „v produkci“ obecně. Pole Serilogu (`WriteTo`) se navíc přes další JSON soubor
  přepisuje nešikovně – proměnná `Serilog__WriteTo__1__Args__path` změní jen cestu.
- **Forwarded headers bez kódu.** Za reverse proxy (Caddy, krok 5) stačí vestavěná proměnná
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, nastaví se v Compose.
- **Sklad nemá Data Protection** – nemá formuláře ani cookies.

## Známá omezení

- **Klíče Data Protection nejsou šifrované** (varování `No XML encryptor configured`). Leží na volume, ke kterému
  má přístup jen VM. Pro výukový projekt s jedním uživatelem stačí.
- **Varování `Failed to determine the https port for redirect`.** Kontejner poslouchá jen na HTTP, takže
  `UseHttpsRedirection` nemá kam přesměrovat a nic nedělá. HTTPS zajistí Caddy (krok 5), za ním se varování
  neprojeví.
- **Image nemá `curl`**, takže Docker healthcheck nejde napsat obvyklým `curl /health` – řeší krok 2.
- **Velikost image ~360 MB** (rozbalená). Menší by byly varianty `-alpine` nebo `-chiseled`; zatím zbytečná
  komplikace (chiseled nemá shell, hůř se ladí).
- Služby se stále nenajdou navzájem samy – spojení kontejnerů a RabbitMQ do jedné sítě je krok 2.

## Jak ověřit

```bash
# build obou image (z kořene repozitáře)
docker build -f src/MiniShop.Inventory.Api/Dockerfile -t minishop-inventory-api .
docker build -f src/MiniShop.Web/Dockerfile -t minishop-web .

# sklad proti lokálnímu RabbitMQ (docker start minishop-rabbit), data na volume
docker run -d --name mi-inv -p 8081:8080 -v minishop-inventory-data:/data \
  -e "ConnectionStrings__messaging=amqp://guest:guest@host.docker.internal:5672" minishop-inventory-api
curl http://localhost:8081/health          # Healthy

# Web: RabbitMQ i sklad přes host.docker.internal
docker run -d --name mi-web -p 8080:8080 -v minishop-web-data:/data \
  -e "ConnectionStrings__messaging=amqp://guest:guest@host.docker.internal:5672" \
  -e "Services__inventory-api__http__0=http://host.docker.internal:8081" minishop-web
# http://localhost:8080 → Objednávky, nová objednávka → za ~5 s Confirmed

# úklid
docker rm -f mi-web mi-inv
docker volume rm minishop-web-data minishop-inventory-data
```

V Git Bash na Windows dejte před `docker exec … /data` proměnnou `MSYS_NO_PATHCONV=1`, jinak Git Bash přepíše
`/data` na `C:/Program Files/Git/data`.

`dotnet build MiniShop.slnx` + `dotnet test MiniShop.slnx`: 160 testů prošlo.

## Vyzkoušejte sami

1. **Kontejner je dočasný.** Spusťte sklad **bez** `-v …:/data`, přidejte produkt (`POST /api/products`),
   kontejner smažte a spusťte znovu. Produkt je pryč – databáze byla uvnitř kontejneru. Pak totéž s volume.
2. **Kdo jsem?** `docker exec mi-inv id` → `uid=1654(app)`. Zkuste `docker exec mi-inv touch /app/x` –
   aplikace nesmí přepsat vlastní soubory.
3. **Cache vrstev.** Změňte text v `Pages/Index.cshtml` a postavte image Webu znovu. Ve výpisu uvidíte
   `CACHED` u `dotnet restore` – stahování balíčků se přeskočilo.
4. **Konfigurace zvenku.** Spusťte Web s `-e "Serilog__MinimumLevel__Default=Debug"` a podívejte se do
   `docker logs mi-web`. Stejná image, jiné chování – bez rebuildu.
5. **Ztracené klíče.** Spusťte Web bez volume, otevřete formulář objednávky, kontejner restartujte
   (`docker rm -f` + `docker run`) a formulář odešlete. Uvidíte chybu 400 (neplatný antiforgery token).
   S volume se to nestane.
6. **Co je v image?** `docker history minishop-web` ukáže vrstvy a jejich velikost. SDK tam není.
