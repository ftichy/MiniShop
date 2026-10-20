# Cloud krok 8: CD – automatické nasazení

Po kroku 7 vznikaly image samy, ale na VM se nasazovaly ručně: změnit `IMAGE_TAG`, `pull`, `up`, zkontrolovat,
případně vrátit. Teď to po každém sloučení do `main` udělá **GitHub Actions** – pokaždé stejně, včetně kontroly
a návratu na předchozí verzi. A přitom je VM z internetu **zavřenější než předtím**: port 22 není veřejný vůbec,
GitHub (i vy) chodí na VM soukromou sítí **Tailscale**.

## Jak to funguje

Přirovnání: dosud jste zboží ze skladu (GHCR) vozil do prodejny sám. Teď ho rozveze **kurýr** (GitHub Actions).
Do areálu ho pustí jen služební bránou s čipem (Tailscale), má klíč jen od zadního skladu (uživatel `deploy`,
který smí spustit jediný skript), po vyložení zkontroluje, že regály stojí (healthchecky + smoke test), a když ne,
vrátí původní zboží (rollback).

```
merge do main
   │
   ▼  ci.yml
Build a testy ─✅─► Image web / inventory-api (push sha-abc1234) ─✅─► Nasazení (deploy.yml, environment production)
                                                                     │
   runner GitHubu ──① tailscale/github-action (OAuth, tag:ci, dočasný uzel)──► soukromá síť Tailscale
                  ──② ssh deploy@100.126.167.103 "abc1234…(40 znaků)"──────► VM (port 22 jen přes Tailscale)
                                                                     │
   VM: authorized_keys: restrict,command="/usr/local/bin/minishop-deploy"  ← klíč nic jiného neumí
       minishop-deploy (= deploy/deploy.sh):
         ověří vstup → stáhne deploy/compose*.yaml + Caddyfile z GitHubu PRO TENTO COMMIT
         → .env IMAGE_TAG=sha-abc1234 → docker compose pull → up -d --wait
         → neprošly healthchecky? vrátí předchozí konfiguraci a tag, skončí chybou
                                                                     │
   runner ──③ smoke test: https://minishop-tichy.duckdns.org → 401 (platný certifikát, Caddy, zámek)
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Continuous Delivery / Deployment** | každá změna je připravená k nasazení / nasadí se sama | Deployment – po sloučení do `main` bez zásahu |
| **Push model** | pipeline se přihlásí na server a nasadí | runner → SSH → VM (opak: pull model, server si hlídá nové verze) |
| **Environment** | cíl nasazení s vlastními secrets a pravidly | `production`: jen z `main`, secrets `TS_OAUTH_*`, `DEPLOY_SSH_KEY`, proměnné `DEPLOY_HOST`, `DEPLOY_KNOWN_HOSTS` |
| **Reusable workflow** | workflow volaný jiným workflow | `deploy.yml` volá `ci.yml` (job `deploy`) i člověk tlačítkem |
| **Tailscale** | soukromá šifrovaná síť (WireGuard) mezi vašimi zařízeními | PC `notehome`, VM `minishop-vm` (`tag:minishop-vm`), runner na dobu běhu (`tag:ci`) |
| **ACL / grants** | kdo smí v Tailscale kam | vy → VM:22, `tag:ci` → VM:22, nic jiného |
| **Vynucený příkaz** | `command=` v `authorized_keys` – klíč spustí jen daný program | `minishop-deploy`; poslaný text dostane v `SSH_ORIGINAL_COMMAND` |
| **known_hosts** | otisk serveru, který klient čeká | proměnná `DEPLOY_KNOWN_HOSTS` – podvržený server SSH odmítne |
| **Smoke test** | rychlá kontrola „žije to?“ po nasazení | `up --wait` (healthchecky) + HTTPS zvenku |
| **Rollback** | návrat na předchozí verzi | automaticky v `deploy.sh`; ručně *Actions → Deploy → Run workflow* se starším SHA |

**Proč Tailscale a ne otevřený port 22:** runner GitHubu má pokaždé jinou IP z tisíců rozsahů – povolit „jen
GitHub“ v security listu nejde. Otevřít 22 celému internetu by fungovalo (přihlášení jen klíčem), ale port by
ostřelovaly stovky botů denně. S Tailscale vedou z internetu na VM jen 80 a 443. Runner se do sítě připojí
dočasně (OAuth klient smí vytvářet jen uzly se štítkem `tag:ci`) a po běhu z ní zmizí.

**Proč vlastní uživatel `deploy` s vynuceným příkazem:** kdyby klíč z GitHubu unikl, útočník neumí spustit
shell ani nic jiného – jen „nasaď MiniShop z commitu X“, kde X musí být 40 hexa znaků a konfigurace se stahuje
z vašeho repozitáře. Pozor: `deploy` je ve skupině `docker`, a to je prakticky root – omezení drží jen díky tomu,
že klíč nedostane shell.

**Proč se konfigurace stahuje z GitHubu podle commitu:** `compose.yaml`, `compose.cloud.yaml` a `Caddyfile`
na VM tak vždy odpovídají nasazené verzi kódu (dřív se kopírovaly ručně a mohly se rozejít). `.env` s hesly
zůstává jen na VM – do gitu nepatří.

**EDA bonus:** při nasazení se Web a sklad restartují. Rozjetá objednávka se neztratí – zpráva počká v outboxu
(odeslání) nebo ve frontě RabbitMQ (zpracování) a inbox zajistí, že se nezpracuje dvakrát. Viz *Vyzkoušejte sami*.

## Návod krok za krokem

### A. Tailscale (vy)

- [ ] **A1.** https://tailscale.com → přihlášení (GitHub/Google) → aplikace Tailscale na PC, přihlásit.
- [ ] **A2.** Admin konzole (https://console.tailscale.com) → ☰ → **Access controls** → *JSON editor* → nahradit:

  ```json
  {
    "tagOwners": {
      "tag:minishop-vm": ["autogroup:admin"],
      "tag:ci":          ["autogroup:admin"]
    },
    "grants": [
      { "src": ["autogroup:member"], "dst": ["tag:minishop-vm"], "ip": ["tcp:22"] },
      { "src": ["tag:ci"],           "dst": ["tag:minishop-vm"], "ip": ["tcp:22"] }
    ]
  }
  ```

- [ ] **A3.** ☰ → Settings → **Trust credentials** (dřív *OAuth clients*) → **+ Credential** → OAuth: popis
  `github-actions-minishop`, scope **Keys → Auth Keys: Write**, tag **`tag:ci`** → Client ID a **Client secret**
  (zobrazí se jen jednou).

### B. GitHub environment `production` (vy)

- [ ] **B1.** Repozitář → Settings → **Environments** → *New environment* `production`.
- [ ] **B2.** *Deployment branches and tags* → **Selected branches and tags** → `main`.
- [ ] **B3.** *Environment secrets* (pole **Name** = jméno, **Value** = tajná hodnota – nezaměnit):
  `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET`, `DEPLOY_SSH_KEY` (viz D2).
- [ ] **B4.** *Environment variables*: `DEPLOY_HOST` = Tailscale IP VM, `DEPLOY_KNOWN_HOSTS` = řádek z
  `ssh-keyscan -t ed25519 <Tailscale IP VM>` (spuštěno na VM; otisk porovnat s tím, co zná PC).

### C. VM do Tailscale (Claude přes ssh-agent / vy)

- [ ] **C1.** Na VM:

  ```bash
  curl -fsSL https://tailscale.com/install.sh | sh
  sudo tailscale up --hostname=minishop-vm --advertise-tags=tag:minishop-vm --accept-dns=false
  ```

  Vypíše odkaz `https://login.tailscale.com/a/…` → otevřít, *Connect*. `tailscale ip -4` → `100.x.y.z`. 📝
  `--accept-dns=false`: VM si nechá svůj DNS (Docker a OCI ho potřebují).
- [ ] **C2.** Z PC: `ssh -i ~/.ssh/minishop_oci ubuntu@100.x.y.z` → funguje přes Tailscale.
  (Tailscale si v iptables sám vloží řetězec `ts-input` před Oraclův REJECT.)

### D. Uživatel `deploy` (Claude / vy)

- [ ] **D1.** Na VM: uživatel `deploy` (bez hesla, skupina `docker`), aplikace z `~/minishop` do `/opt/minishop`
  (vlastník `deploy`, `.env` 600), `deploy/deploy.sh` jako `/usr/local/bin/minishop-deploy` (vlastník root, 755).
  Compose projekt se jmenuje `minishop` (`name:` v `compose.yaml`) → kontejnery i volumes se převezmou.
- [ ] **D2.** Na PC klíč **bez passphrase** (2× Enter), soukromý do secretu, pak smazat:

  ```powershell
  ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\minishop_deploy -C "github-actions-deploy"
  Get-Content $env:USERPROFILE\.ssh\minishop_deploy -Raw | Set-Clipboard     # → secret DEPLOY_SSH_KEY
  Remove-Item $env:USERPROFILE\.ssh\minishop_deploy; Set-Clipboard -Value " "
  ```

- [ ] **D3.** Na VM `/home/deploy/.ssh/authorized_keys` (vlastník deploy, 600), jeden řádek:

  ```
  restrict,command="/usr/local/bin/minishop-deploy" ssh-ed25519 AAAA… github-actions-deploy
  ```

- [ ] **D4.** Zkouška skriptu ručně: `sudo -u deploy /usr/local/bin/minishop-deploy <celý SHA z main>` → *Hotovo*.
  A `… minishop-deploy 'abc; rm -rf /'` → *Chybný vstup*, exit 2.

### E. PR, sloučení a první automatické nasazení (vy)

- [ ] **E1.** Větev `cd/krok8-nasazeni` → commit → push → PR → ✅ *Build a testy*, *Image …* → **Merge**.
- [ ] **E2.** *Actions* → běh na `main`: *Build a testy* → *Image …* (push) → **Nasazení / Nasazení na VM**.
  V detailu: *Připojení do Tailscale*, *Nasazení (deploy.sh na VM)* s výpisem z VM, *Smoke test zvenku* → 401.
- [ ] **E3.** Repozitář vpravo *Deployments* → `production` s odkazem na web a commitem.

### F. Zavření portu 22 (vy, až po E2)

- [ ] **F1.** OCI → security list → pravidlo **TCP 22** → **Remove**. Z internetu vedou na VM jen 80 a 443.
- [ ] **F2.** Na PC `~/.ssh/config`: `HostName` u `minishop-vm` na Tailscale IP (`100.x.y.z`). `ssh minishop-vm` dál
  funguje – odkudkoli, kde běží Tailscale, i když se změní domácí IP.
- [ ] **F3.** Zkouška: `ssh -i ~/.ssh/minishop_oci ubuntu@<veřejná IP>` → timeout. Správně.

**Nouzový přístup:** kdyby Tailscale na VM nefungoval, v security listu se dá pravidlo pro 22 z vaší IP kdykoli
přidat zpět (je to cloudový firewall, ne VM).

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `.github/workflows/deploy.yml` | workflow „Deploy“ (volaný z CI i ručně): Tailscale → SSH s pevným `known_hosts` → `minishop-deploy` → smoke test; environment `production`, `concurrency: production` bez rušení |
| `deploy/deploy.sh` | skript nasazení na VM (`/usr/local/bin/minishop-deploy`): kontrola SHA, konfigurace z GitHubu pro commit, `pull`, `up --wait`, rollback, `flock`, log `/opt/minishop/deploy.log` |
| `docs/kroky-cloud/krok-08-cd.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `.github/workflows/ci.yml` | job `deploy` (volá `deploy.yml`) po `images`, jen na `main`; `paths-ignore` `docs/**`, `**/*.md` jen pro push; `cancel-in-progress` jen u PR |

Mimo repozitář: Tailscale (ACL, OAuth klient, VM `tag:minishop-vm`), GitHub environment `production`,
na VM uživatel `deploy`, `/opt/minishop`, `/usr/local/bin/minishop-deploy`; security list bez portu 22.

## Rozhodnutí v tomto kroku

- **Continuous Deployment bez ručního schválení** – výukový projekt, testy a image prošly; schvalování (*Required
  reviewers*) jde v environmentu kdykoli zapnout a z CD se stane Continuous Delivery.
- **Tailscale místo veřejného SSH** – z internetu jen 80/443; SSH pro vás funguje odkudkoli bez úprav security listu.
- **OAuth klient Tailscale** (secret v GitHubu). Bez tajemství by šlo přes OIDC (*workload identity federation*) –
  krok 10.
- **Vynucený příkaz + uživatel `deploy`** – klíč z GitHubu neumí nic jiného než nasadit commit.
- **Konfigurace z GitHubu podle commitu** (`raw.githubusercontent.com/<repo>/<sha>/deploy/…`) – funguje, protože
  repozitář je veřejný; žádné `scp`, žádný rozjezd verzí.
- **Rollback na VM, ne v pipeline** – skript zná předchozí stav (`previous/`) a vrátí ho i bez spojení s GitHubem.
- **Smoke test přes 401** – `/health` je za zámkem; 401 dokazuje DNS, certifikát a Caddy. Zdraví aplikace
  hlídají healthchecky (`up --wait`).
- **Nasazení se nepřerušuje** – `concurrency: production` s `cancel-in-progress: false`; v CI se ruší jen běhy PR.
- **Změny dokumentace nenasazují** (`paths-ignore`); v PR se testy spouští vždy (povinný check).

## Známá omezení

- **Krátký výpadek při nasazení** – `up -d` restartuje Web a sklad (několik sekund 502 z Caddy). Bez dvou instancí
  (blue/green) se mu nevyhneme.
- **Rollback neumí vrátit databázi** – při změně schématu (`EnsureCreated`, viz `CLAUDE.md`) je potřeba ruční zásah.
- **`deploy` je ve skupině `docker`** (≈ root). Chrání ho vynucený příkaz, ne oprávnění.
- **Závislost na Tailscale** – výpadek jejich koordinačního serveru = nasazení neproběhne (aplikace běží dál).
- **Smoke test nekontroluje funkčnost** (objednávku) – jen že web odpovídá.
- **`deploy.sh` na VM se neaktualizuje sám** – změna skriptu v repozitáři = ručně nainstalovat (`install … /usr/local/bin/minishop-deploy`).
- **Image `latest` se nasazuje jen přes `sha-…`** – ruční rollback potřebuje celý SHA (z historie commitů).

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| Tailscale admin → Machines | `notehome`, `minishop-vm` (`tag:minishop-vm`), během nasazení dočasně `github-…` (`tag:ci`) |
| Běh CI na `main` | *Build a testy* → *Image web*, *Image inventory-api* → *Nasazení / Nasazení na VM* ✅ |
| Log kroku *Nasazení* | `Nasazuji sha-…`, `Hotovo: sha-…`, tabulka služeb |
| `/opt/minishop/deploy.log` na VM | řádek pro každé nasazení |
| Repozitář → *Deployments* | `production`, aktivní, odkaz na https://minishop-tichy.duckdns.org |
| `ssh ubuntu@<veřejná IP>` | timeout (port 22 zavřený) |
| `ssh minishop-vm` (přes Tailscale) | funguje |
| `ssh -i <deploy klíč> deploy@<vm>` s čímkoli jiným než SHA | *Chybný vstup* (shell nedostanete) |

## Vyzkoušejte sami

1. **Změna textu až do cloudu.** Na větvi změňte nadpis na úvodní stránce (`src/MiniShop.Web/Pages/Index.cshtml`),
   PR → Merge → za ~6 minut je změna na https://minishop-tichy.duckdns.org, aniž byste šli na server.
2. **Ruční rollback.** *Actions* → **Deploy** → *Run workflow* → SHA předchozího commitu z `main` → na webu je stará
   verze. Pak zpět na nejnovější SHA.
3. **Automatický rollback.** Na pokusné větvi rozbijte healthcheck (např. v `compose.yaml` u `web` změňte port
   v testu na `8081`), sloučit nejde bez zeleného CI – ale CI to nepozná! Proto pozor: po sloučení `up --wait`
   selže, `deploy.sh` vrátí předchozí verzi a běh skončí ❌. Web celou dobu běží. (Pak opravit dalším PR.)
4. **EDA: zpráva se neztratí.** Založte objednávku a hned spusťte nasazení (ruční *Run workflow*). Objednávka
   po restartu dojde do *Confirmed* – zprávy počkaly v outboxu a v RabbitMQ.
5. **Kdo je v síti.** Tailscale admin → *Machines* během nasazení: dočasný uzel GitHubu se objeví a po běhu zmizí.
