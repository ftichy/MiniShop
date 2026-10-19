# Cloud krok 2: Docker Compose – „produkce na mém PC“

Po kroku 1 uměla každá služba běžet v kontejneru, ale spouštěly se ručně a adresy se zadávaly v příkazu.
Teď celý MiniShop – RabbitMQ, sklad a Web – popisuje jeden soubor `deploy/compose.yaml` a spustí ho jeden
příkaz `docker compose up`. Je to přesně to, co v kroku 4 poběží na VM v cloudu. Aspire zůstává pro vývoj.

## Jak to funguje

Přirovnání: v kroku 1 jsme měli spotřebiče v krabicích. Compose je **plán kuchyně** – kam který spotřebič patří,
co se do čeho zapojuje, v jakém pořadí se zapínají a kde je lednice. Kdokoli podle plánu postaví stejnou kuchyň.

Compose je obdoba AppHostu z kroku 10B, jen jako YAML místo C#:

| Aspire AppHost (`AppHost.cs`) | Compose (`compose.yaml`) |
| --- | --- |
| `AddRabbitMQ("messaging")` | služba `rabbitmq` (image `rabbitmq:4-management`) |
| `AddProject<…>("inventory-api")` | služba `inventory-api` (`build:` z Dockerfile kroku 1) |
| `WithReference(messaging)` | `ConnectionStrings__messaging: amqp://…@rabbitmq:5672` |
| `WithReference(inventoryApi)` | `Services__inventory-api__http__0: http://inventory-api:8080` |
| `WaitFor(messaging)` | `depends_on: rabbitmq: condition: service_healthy` |
| `WithHttpHealthCheck("/health")` | `healthcheck:` (bash + `/dev/tcp`) |
| `WithLifetime(Persistent)` | pojmenovaný volume `rabbitmq-data` |
| heslo v user secrets AppHostu | heslo v `deploy/.env` (mimo git) |

```
  docker compose up -d --build
            │
            ▼
  ┌─────────── síť minishop_default (Compose ji vytvoří sám) ────────────┐
  │                                                                       │
  │  rabbitmq ◄─────────── amqp://minishop:…@rabbitmq:5672 ────┐          │
  │   healthy ①                                                │          │
  │      │                                                     │          │
  │      ▼ depends_on                                          │          │
  │  inventory-api :8080   ◄──── http://inventory-api:8080 ── web :8080 ◄─┼── localhost:8080
  │   healthy ②                                              healthy ③   │
  │   /data ─► volume inventory-data                 /data ─► web-data   │
  └───────────────────────────────────────────────────────────────────────┘
  127.0.0.1:15673 ─► rabbitmq:15672 (management UI, jen z tohoto počítače)
```

**Síť a jména.** Compose vytvoří síť `minishop_default` a v ní funguje DNS: jméno služby (`rabbitmq`,
`inventory-api`) je zároveň adresa kontejneru. Proto stačí v proměnné napsat `http://inventory-api:8080`.

**Porty (`ports:`)** otevírají kontejner ven z Dockeru. Ven vede jen Web (8080) a management UI RabbitMQ
(jen na `127.0.0.1`). Sklad a port 5672 nejsou zvenku dostupné vůbec – mluví s nimi jen ostatní kontejnery.
Co není potřeba vidět, to není vidět. V cloudu to bude důležité.

**Pořadí startu a healthcheck.** `depends_on` s `condition: service_healthy` čeká, až Docker označí závislost
za *healthy*. U RabbitMQ to zjišťuje `rabbitmq-diagnostics ping`. U .NET služeb voláme `/health`, ale image
nemá `curl`. Bash ale umí otevřít TCP spojení přes speciální cestu `/dev/tcp/host/port`, takže HTTP
požadavek pošleme ručně:

```bash
exec 3<>/dev/tcp/127.0.0.1/8080             # otevři spojení jako "soubor" číslo 3
printf 'GET /health HTTP/1.0\r\n\r\n' >&3    # pošli HTTP požadavek
grep -q ' 200 ' <&3                          # v odpovědi hledej "HTTP/1.1 200 OK"
```

Stejná kontrola pro obě služby je v YAML jednou jako **kotva** (`&dotnet-healthcheck`) a použitá přes `*`.

**Volumes.** Tři pojmenované volumes (`rabbitmq-data`, `inventory-data`, `web-data`) přežijí
`docker compose down`, restart počítače i nový build image. Smaže je jen `docker compose down -v`.

**Restart.** `restart: unless-stopped` – když služba spadne nebo se restartuje Docker (na VM: reboot),
kontejner se spustí znovu sám. Neplatí jen pro ručně zastavené kontejnery.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `deploy/compose.yaml` | služby `rabbitmq`, `inventory-api`, `web`, healthchecky, volumes, porty |
| `deploy/.env.example` | vzor konfigurace: `RABBITMQ_USER`, `RABBITMQ_PASSWORD`, `WEB_PORT`, `IMAGE_TAG` |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `.gitignore` | `.env` (tajné hodnoty pro Compose) |
| `.dockerignore` | `deploy/` a `**/.env` – konfigurace nasazení ani hesla nejdou do build kontextu |

Kód aplikace se v tomto kroku neměnil – vše připravil krok 1 (konfigurace z proměnných prostředí).

## Rozhodnutí v tomto kroku

- **Healthcheck přes bash `/dev/tcp`, ne `curl`.** Instalace `curl` do image znamená další balíčky
  (a jejich bezpečnostní záplaty) jen kvůli kontrole stavu. Bash v image je.
- **`/health`, ne `/alive`.** `/health` obsahuje i `OutboxHealthCheck` (čte outbox z databáze), takže ověří,
  že služba nastartovala včetně DB. `Degraded` vrací 200 (projde), `Unhealthy` 503 (neprojde).
- **Vlastní uživatel RabbitMQ.** `guest` smí v RabbitMQ jen z localhostu uvnitř kontejneru – ostatní kontejnery
  se jako `guest` nepřihlásí. Uživatele vytvoří RabbitMQ při prvním startu z `RABBITMQ_DEFAULT_USER/PASS`.
- **Povinné proměnné `${RABBITMQ_PASSWORD:?…}`.** Bez `.env` Compose odmítne start s jasnou chybou,
  místo aby služby padaly na přihlášení do RabbitMQ.
- **`hostname: rabbitmq`.** RabbitMQ ukládá data do složky podle jména uzlu (`rabbit@<hostname>`).
  Bez pevného jména by nový kontejner dostal náhodné a fronty ve volume by „nenašel“.
- **Management UI na portu 15673, jen `127.0.0.1`.** 15672 už používá `minishop-rabbit` (spouštění bez Aspire).
  Na VM nebude UI dostupné z internetu – přístup přes SSH tunel (krok 4/5).
- **`image:` + `build:` v jednom souboru.** Lokálně `--build` image postaví a pojmenuje
  `minishop-web:local`. V kroku 7 se `image` změní na GHCR a server bude image jen stahovat.
- **Soubor v `deploy/`, ne v kořeni.** Kořen patří vývoji (solution, Aspire); `deploy/` je to, co se kopíruje
  na server (krok 4, 8).

## Známá omezení

- **Změna hesla v `.env` nezmění existující účet v RabbitMQ.** `RABBITMQ_DEFAULT_*` platí jen pro prázdný volume.
  Změna hesla = `rabbitmqctl change_password` v kontejneru (nebo smazat volume `minishop_rabbitmq-data`,
  tím zmizí i fronty). Rotaci hesel řeší krok 10.
- **Heslo jen z písmen a číslic** – je součástí adresy `amqp://user:heslo@rabbitmq`; `@ : /` by ji rozbily.
- **Healthcheck nehlídá RabbitMQ.** `/health` obsahuje jen `OutboxHealthCheck`, a ten hlásí nejvýš `Degraded`
  (HTTP 200) – a nedostupný broker se ani nepočítá jako neúspěšný pokus. Při výpadku brokeru jsou tedy služby
  dál `healthy`. Pro pořadí startu to nevadí (`depends_on` čeká na healthy RabbitMQ), pro monitoring ano – krok 11.
- **Bez HTTPS a bez přihlášení**, Web na `http://localhost:8080`. Na lokálním PC v pořádku; v cloudu řeší krok 5.
- **Bez telemetrie.** Není nastavený `OTEL_EXPORTER_OTLP_ENDPOINT`, trasy a metriky se neexportují (krok 11).
  Logy: `docker compose logs`, soubory v `/data/logs` na volumes.
- **Úplně čistý start = smazat volumes** (`docker compose down -v`). Při změně schématu DB (`EnsureCreated`)
  je to stejné jako mazání `*.db` souborů lokálně.
- Změna kódu = `docker compose up -d --build` (rebuild image, restart změněných služeb).

## Jak ověřit

```bash
cd deploy
cp .env.example .env                   # jen poprvé; změňte heslo
docker compose up -d --build           # build image + start (první build ~1–2 min)
docker compose ps                      # všechny tři služby "healthy"
# http://localhost:8080 → Objednávky → nová objednávka → za ~5 s Confirmed

docker compose down                    # zastavit (volumes zůstanou)
docker compose up -d                   # znovu – objednávky jsou pořád tam
```

Ověřeno: start v pořadí rabbitmq → inventory-api → web, katalog se po startu sám načetl (6 produktů přes
`SyncCatalogV1`), objednávka `Confirmed`, po `down` + `up` data zůstala. Paměť celého stacku ~250 MB
(Web 69 MB, sklad 57 MB, RabbitMQ 126 MB).

## Vyzkoušejte sami

1. **EDA v praxi: výpadek skladu.** `docker compose stop inventory-api`, odešlete objednávku. Zůstane
   `Pending` a v management UI (http://localhost:15673, údaje z `.env`) uvidíte ve frontě
   `minishop.inventory` čekající zprávu. `docker compose start inventory-api` → objednávka se do pár sekund
   potvrdí. (Ověřeno.) Když necháte sklad vypnutý déle než 30 s, sága objednávku zamítne (timeout).
2. **DNS v síti Compose.** `docker compose exec web getent hosts inventory-api rabbitmq` – vypíše IP adresy
   kontejnerů. Zkuste totéž z vašeho PC (`ping inventory-api`) – nefunguje, jména platí jen uvnitř sítě.
3. **Co je vidět zvenku.** `curl http://localhost:8080/health` funguje, ale sklad na žádném portu
   z vašeho PC nedosáhnete. Zkuste přidat ke skladu `ports: ["8081:8080"]` a `docker compose up -d`.
4. **Výpadek brokeru.** `docker compose stop rabbitmq`, odešlete objednávku a po ~45 s
   `docker compose start rabbitmq`. Objednávka skončí `Rejected` – příkaz `ReserveStockV1` čekal v outboxu,
   ale sága mezitím po 30 s zamítla objednávku timeoutem (v logu `[Sága] … sklad neodpověděl včas`).
   Consumery se po návratu brokeru samy připojí a další objednávka projde. Všimněte si, že `docker compose ps`
   celou dobu ukazuje `healthy` (viz Známá omezení). Detail kontrol:
   `docker inspect minishop-web-1 --format '{{json .State.Health}}'`. (Ověřeno.)
5. **Restart policy.** Simulace pádu: `docker compose exec web bash -c 'kill -TERM 1'` – proces skončí
   a Docker kontejner sám spustí znovu (`docker inspect minishop-web-1 -f '{{.RestartCount}}'` → 1).
   Naopak `docker compose kill web` nebo `stop web` Docker bere jako ruční zastavení a kontejner nechá
   vypnutý. (Ověřeno.)
6. **Smazání dat.** `docker compose down -v` a `up -d` – prázdné objednávky, sklad znovu naseedovaný,
   katalog se znovu načte. Tohle v produkci nikdy (krok 12 = zálohy).
