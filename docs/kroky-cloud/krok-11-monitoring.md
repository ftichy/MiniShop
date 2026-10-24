# Cloud krok 11: Monitoring a logy v cloudu

Lokálně vidíte v Aspire dashboardu, co se v MiniShopu děje: trasu objednávky přes Web, RabbitMQ a sklad, logy
u každého kroku, metriky outboxu. Na VM dosud telemetrie nešla nikam – chybu v sáze jste v cloudu hledal jen
v `docker compose logs`. A o výpadku byste se dozvěděl, až byste web náhodou otevřel. Teď na VM poběží
**stejný Aspire dashboard** a **tři hlídači** pošlou e-mail, když něco nefunguje.

## Jak to funguje

Přirovnání: dashboard je **kamerový systém uvnitř obchodu** – vidíte, kudy šel každý zákazník. UptimeRobot je
**soused**, který každých 5 minut zkusí kliku u dveří, a když se neotevřou, zavolá vám. Alarmy OCI jsou
**hlásič kouře** v budově – ozvou se, když se přehřívá technika. Kamery nepomůžou, když obchod vyhoří;
proto je soused venku.

```
                       internet
                          │
    UptimeRobot ──5 min──►│ https://minishop-tichy.duckdns.org/health            (bez hesla) → web /health
    (e-mail)              │ https://minishop-tichy.duckdns.org/health/inventory  (bez hesla) → inventory-api /health
                          │ https://minishop-tichy.duckdns.org/…                 (heslo)     → web
    vy (prohlížeč) ──────►│ https://dashboard.minishop-tichy.duckdns.org         (heslo)     → aspire-dashboard:18888
                          ▼
   VM ─ Caddy ──────────────────────────────────────────────────────────────────────────────────
        web, inventory-api ──OTLP (gRPC, jen síť Compose)──► aspire-dashboard:18889 (telemetrie v paměti)
        Oracle Cloud Agent ──metriky CPU/paměť──► OCI Monitoring ──alarm──► OCI Notifications ──► e-mail
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **OTLP** | standardní protokol OpenTelemetry pro trasy, logy a metriky | služby → `aspire-dashboard:18889` |
| **Samostatný Aspire dashboard** | stejný dashboard jako u AppHostu, jen jako kontejner | `mcr.microsoft.com/dotnet/aspire-dashboard:13.6` |
| **Health check** | adresa, která řekne, jestli je služba v pořádku | `/health` = `Healthy` / `Degraded` (outbox visí > 5 min) / `Unhealthy` |
| **Externí monitoring** | kontrola zvenku, nezávislá na VM | UptimeRobot, keyword `Healthy` každých 5 min |
| **Alarm (OCI Monitoring)** | dotaz nad metrikami, který při splnění pošle zprávu | CPU > 80 %, paměť > 90 %, VM neposílá metriky – vždy 15 min |
| **MQL** | dotazovací jazyk metrik OCI | `CpuUtilization[5m]{resourceId = "…"}.mean() > 80` |

**Kód se nemění.** `ServiceDefaults` z kroku 11 EDA exportují OTLP, jakmile je nastavená proměnná
`OTEL_EXPORTER_OTLP_ENDPOINT` – lokálně ji dává Aspire, v Compose teď `compose.yaml`. Jméno služby v dashboardu
dává `OTEL_SERVICE_NAME`.

**Proč `/health` bez hesla:** hlídač zvenku heslo nezná (a ukládat ho do cizí služby nechceme). Caddy pustí bez
zámku jen dvě přesné adresy; odpověď je jedno slovo, žádná data. Vše ostatní (i `/healthx`) chce heslo.

**Proč keyword „Healthy“ a ne jen „odpovídá“:** stav *Degraded* (zaseknutý outbox – zprávy neodcházejí) vrací
HTTP 200. Obyčejná kontrola by ho nepoznala, kontrola slova `Healthy` ano. Pozor na velikost písmen:
`Unhealthy` obsahuje `healthy` – proto **case sensitive**.

**Proč subdoména a ne `/dashboard`:** Aspire dashboard neumí běžet pod podsložkou. DuckDNS obslouží libovolnou
subdoménu vašeho jména (`*.minishop-tichy.duckdns.org` → stejná IP) a Caddy pro ni sám získá certifikát.

## Návod krok za krokem

### A. PR a nasazení (vy / Claude)

- [ ] **A1.** Commit → push větve `monitoring/krok11` → PR → ✅ *Build a testy* → **Merge**.
- [ ] **A2.** CI na `main` → *Nasazení* ✅. Smoke test teď kontroluje i `/health` a `/health/inventory` (`Healthy`).

### B. Dashboard v cloudu (vy)

- [ ] **B1.** https://dashboard.minishop-tichy.duckdns.org → stejné jméno a heslo jako MiniShop → *Structured logs*.
  První načtení může trvat ~10 s (Caddy žádá o certifikát pro subdoménu).
- [ ] **B2.** Na MiniShopu vytvořte objednávku → v dashboardu *Traces* → filtr `minishop.order.id` = číslo objednávky
  → trasa přes `web` i `inventory-api` (POST, `publish …`, `process …` v obou službách).

### C. UptimeRobot (vy)

- [ ] **C1.** https://uptimerobot.com → *Sign up* (zdarma, 50 monitorů, interval 5 min) → potvrdit e-mail.
- [ ] **C2.** *New monitor* → typ **Keyword** → URL `https://minishop-tichy.duckdns.org/health`,
  Keyword `Healthy`, **Alert when: Keyword not exists**, **Case sensitive** ✅, interval 5 min, upozornění e-mailem.
- [ ] **C3.** Totéž pro `https://minishop-tichy.duckdns.org/health/inventory` (název „MiniShop – sklad“).

### D. Alarmy OCI (vy)

- [ ] **D1.** Do `infra/terraform.tfvars` řádek `alert_email = "…"`.
- [ ] **D2.** `tofu plan` → `Plan: 5 to add, 0 to change, 0 to destroy` (téma, odběr, 3 alarmy). `tofu apply`.
- [ ] **D3.** E-mail *OCI Notifications – Subscription confirmation* → **Confirm subscription**.
- [ ] **D4.** OCI → *Observability & Management* → *Monitoring* → *Alarm status* → 3 alarmy, stav *OK*.
  Kdyby u nich bylo *No data*, VM metriky neposílá (Oracle Cloud Agent) – ozvěte se.

### E. Zkouška výpadku (vy)

- [ ] **E1.** `ssh minishop-vm "cd /opt/minishop && sudo -u deploy docker compose stop web"`
- [ ] **E2.** Do ~5–10 minut e-mail od UptimeRobot (*Monitor is DOWN*). Na webu 502.
- [ ] **E3.** `ssh minishop-vm "cd /opt/minishop && sudo -u deploy docker compose start web"` → e-mail *Monitor is UP*.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `infra/monitoring.tf` | téma `minishop-alerts` + e-mailový odběr (OCI Notifications), 3 alarmy (CPU, paměť, chybějící metriky) |
| `docs/kroky-cloud/krok-11-monitoring.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/compose.yaml` | služba `aspire-dashboard` (lokálně `127.0.0.1:18888`, `mem_limit` 512 MB), `OTEL_EXPORTER_OTLP_ENDPOINT` + `OTEL_SERVICE_NAME` pro `web` a `inventory-api` |
| `deploy/compose.cloud.yaml` | dashboard bez publikovaného portu (jen přes Caddy) |
| `deploy/Caddyfile` | `/health` a `/health/inventory` bez hesla, subdoména `dashboard.{$DOMAIN}` vždy se zámkem |
| `.github/workflows/deploy.yml` | smoke test: `/health` a `/health/inventory` musí vrátit `Healthy` |
| `infra/variables.tf`, `infra/terraform.tfvars.example` | proměnná `alert_email` |

Mimo repozitář: účet UptimeRobot se 2 monitory, potvrzený odběr OCI Notifications, `alert_email` v `terraform.tfvars`.

## Rozhodnutí v tomto kroku

- **Aspire dashboard, ne Grafana/Loki/Prometheus** – stejný nástroj jako lokálně, jeden kontejner, nulová
  konfigurace; plnohodnotný stack by na malé VM zabral víc než MiniShop.
- **Telemetrie jen v paměti** – vystačí na „co se stalo před chvílí“; historii řeší logy v souborech (`/data/logs`, 7 dní).
- **Přihlášení k dashboardu přes Caddy** (stejné heslo), dashboard sám `Unsecured` – jedno heslo, žádný token z logu kontejneru.
- **OTLP bez klíče** – port 18889 je jen ve vnitřní síti Compose, z internetu ani z VM nedostupný.
- **Zámek dashboardu i při `BASIC_AUTH=off`** – logy obsahují údaje z objednávek.
- **UptimeRobot, ne healthchecks.io** – aktivně se ptá zvenku (healthchecks.io čeká, až se ozve VM – „dead man's switch“).
- **Alarmy OCI v OpenTofu** – další kus infrastruktury jako kód; po nové VM se alarmy přenastaví samy (nové OCID).
- **15 minut trvání podmínky** – restart nebo build krátce vytíží CPU a alarm by zbytečně budil.

## Známá omezení

- **Restart dashboardu = prázdný dashboard** (i při každém nasazení, kdy se změní jeho konfigurace).
- **Dashboard je jen jeden pohled** – při výpadku VM nevidíte nic; proto externí hlídač.
- **UptimeRobot zdarma kontroluje jen jednou za 5 min** – krátký výpadek mezi kontrolami nezaznamená.
- **`/health` nehlídá RabbitMQ** (krok 2) – spadlý broker poznáte až podle *Degraded* (outbox se zasekne po 5 minutách).
- **Alarm „bez metrik“ spustí i pád Oracle Cloud Agenta**, nejen VM.
- **Logy služeb nejdou do OCI Logging** – jen dashboard (paměť) a soubory na VM (`/data/logs`).
- **E-mail je jediný kanál** – žádné SMS ani Slack (OCI Notifications i UptimeRobot je umí, nepotřebujeme).

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| `curl https://minishop-tichy.duckdns.org/health` | `Healthy` (bez hesla) |
| `curl https://minishop-tichy.duckdns.org/health/inventory` | `Healthy` |
| `curl -o /dev/null -w '%{http_code}' https://minishop-tichy.duckdns.org/` | `401` |
| `curl -o /dev/null -w '%{http_code}' https://dashboard.minishop-tichy.duckdns.org/` | `401` |
| Dashboard → *Resources* / *Structured logs* | `web`, `inventory-api` |
| Dashboard → *Traces*, filtr `minishop.order.id` | trasa objednávky přes obě služby |
| UptimeRobot | 2 monitory *Up* |
| OCI → *Alarm status* | 3 alarmy *OK* |
| `ssh minishop-vm "docker stats --no-stream"` | `aspire-dashboard` pod 512 MB |

Lokálně (krok 2): `cd deploy && docker compose up -d --build` → dashboard na http://localhost:18888.

## Vyzkoušejte sami

1. **Sága v cloudu krok za krokem.** Objednávka → dashboard → trasa → rozklikněte spany `publish OrderPlacedV1`, `process ReserveStockV1`,
   `process StockReservedV1` – stejný obrázek jako lokálně v kroku 11 EDA, jen z cloudu.
2. **Logy k trase.** V detailu spanu *View logs* – jen logy, které patří k té jedné objednávce.
3. **Degraded.** Zastavte RabbitMQ (`docker compose stop rabbitmq`), vytvořte objednávku a počkejte 5+ minut:
   `/health` vrátí `Degraded` (outbox visí) → UptimeRobot pošle e-mail, i když web odpovídá. Pak `start rabbitmq`
   – zprávy z outboxu odejdou a objednávka se dokončí (EDA: nic se neztratí).
4. **Alarm na CPU.** `ssh minishop-vm "timeout 20m sh -c 'yes > /dev/null & yes > /dev/null & wait'"` vytíží obě
   jádra → po ~15 min e-mail od OCI. (Po 20 min se zastaví samo.)
5. **Metriky.** Dashboard → *Metrics* → `web` → `minishop.outbox.delivered`, `minishop.outbox.delay` – kolik zpráv odešlo a jak dlouho čekaly v outboxu.
