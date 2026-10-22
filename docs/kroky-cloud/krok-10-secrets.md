# Cloud krok 10: Secrets a konfigurace

Za kroky 2–9 vzniklo několik tajných hodnot a každá žila jinde: heslo RabbitMQ si vymyslela VM a znala ho jen ona,
otisk hesla do webu ležel na čtyřech místech (`terraform.tfvars`, state, metadata VM, `.env`), klíče na PC
neměly sepsaný postup výměny. Teď secrets uklidíte: **jeden zdroj pravdy** (GitHub environment `production`),
**netajná konfigurace v gitu**, **výměna hesla = změna secretu + spuštění pipeline** a GitHub, který hlídá,
aby se do repozitáře žádný klíč nedostal.

## Jak to funguje

Přirovnání: klíče od firmy. Dřív visely na různých hřebících a jeden měl jen vrátný. Teď jsou v **trezoru**
(GitHub secrets) se **seznamem** (registr `docs/secrets.md`), **kurýr** (Deploy) je ráno doveze tam, kde jsou
potřeba, a výměna zámku je jeden zápis do trezoru a jedna jízda kurýra. U vchodu stojí **detektor kovů**
(push protection), který nepustí do repozitáře nic, co vypadá jako klíč.

```
 GitHub environment production                    git (veřejný)
   secrets: RABBITMQ_PASSWORD, BASIC_AUTH_HASH      deploy/production.env  (doména, uživatelé, prefix image)
            DEPLOY_SSH_KEY, TS_OAUTH_*              deploy/compose*.yaml, Caddyfile
        │                                                  │
        ▼ workflow Deploy                                  │
   printf 'RABBITMQ_PASSWORD=…\nBASIC_AUTH_HASH=…' │ ssh deploy@vm "<sha>"
                                                   ▼
   VM: minishop-deploy  ── SHA z příkazu, secrets ze stdin (jen známé klíče, kontrola tvaru)
        ├─ stáhne deploy/ pro <sha> (i production.env)  ◄──────────┘
        ├─ jiné heslo RabbitMQ? → rabbitmqctl change_password (v běžícím RabbitMQ)
        ├─ .env (600) = production.env + IMAGE_TAG=sha-… + secrets
        └─ pull → up --wait → při chybě vrátí verzi i heslo
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Secret** | hodnota, jejíž únik škodí | hesla, klíče, tokeny – seznam v `docs/secrets.md` |
| **Konfigurace** | nastavení, které tajné není | `deploy/production.env` (v gitu, mění se přes PR) |
| **Zdroj pravdy** | jediné místo, odkud se hodnota bere | GitHub environment `production`; `.env` na VM je jen odvozený |
| **Rotace** | výměna secretu za nový | změnit secret → *Run workflow* Deploy |
| **Secret scanning** | GitHub hledá v repozitáři známé formáty klíčů | upozornění v *Security* |
| **Push protection** | totéž, ale už při `git push` – push s klíčem odmítne | zapnuté pro repozitář |
| **Dependabot** | hlídá závislosti (zranitelnosti + nové verze) | `.github/dependabot.yml`: NuGet, GitHub Actions, Docker |

**Proč secrets přes standardní vstup SSH:** příkaz (`ssh … "<sha>"`) vidí vynucený příkaz v proměnné
`SSH_ORIGINAL_COMMAND`, zapisuje se do logů a na VM i runneru je vidět v seznamu procesů. Standardní vstup ne.
`deploy.sh` přijme jen dva známé klíče a jen v očekávaném tvaru – do `.env` se nedostane nic jiného (ani
`$(příkaz)`). Spuštění ručně z terminálu VM (bez vstupu) vezme hodnoty z dosavadního `.env`, takže ruční
rollback dál funguje.

**Proč RabbitMQ potřebuje zvláštní péči:** `RABBITMQ_DEFAULT_PASS` použije RabbitMQ jen při **prvním** startu
s prázdným volume – pak už má účet uložený u sebe. Nové heslo v `.env` by tedy nestačilo: služby by se přihlašovaly
novým, RabbitMQ by znal staré. Proto `deploy.sh` heslo nejdřív změní v běžícím RabbitMQ a teprve pak restartuje
služby. Když nasazení selže, vrátí staré heslo i verzi.

**Proč OpenTofu už nemá secrets aplikace:** state i metadata VM by je obsahovaly a výměna by znamenala novou VM.
Nová VM teď dostane jen Tailscale (jednorázový klíč) a uživatele `deploy`; MiniShop na ni nasadí první běh Deploy –
stejnou cestou jako každou další verzi.

## Návod krok za krokem

### A. Ochrana repozitáře (vy, GitHub)

- [ ] **A1.** Repozitář → *Settings* → *Advanced Security* (dříve *Code security*):
  - **Dependabot alerts** → *Enable*, **Dependabot security updates** → *Enable*,
  - **Secret Protection** / *Secret scanning* → *Enable*, **Push protection** → *Enable*.
  U veřejného repozitáře je vše zdarma.

### B. Secrets aplikace do GitHubu (vy)

- [ ] **B1.** Nové heslo RabbitMQ do schránky (rovnou nacvičíte rotaci – staré zná jen VM):

  ```powershell
  $b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
  ($b | ForEach-Object { $_.ToString('x2') }) -join '' | Set-Clipboard
  ```

  GitHub → *Settings* → *Environments* → `production` → *Environment secrets* → **Add environment secret**:
  Name `RABBITMQ_PASSWORD`, Value = schránka → *Add secret*. Pak `Set-Clipboard -Value " "`.
- [ ] **B2.** Otisk hesla do webu (stávající, ze `infra/terraform.tfvars`, řádek `basic_auth_hash`) → nový secret
  `BASIC_AUTH_HASH` – jen `$2a$14$…`, bez uvozovek.

### C. Úklid `terraform.tfvars` (vy)

- [ ] **C1.** Z `infra/terraform.tfvars` smazat řádek `basic_auth_hash = …` (a komentář nad ním).
- [ ] **C2.** V `infra/`: `tofu plan` → mění se jen výstup `next_steps` (infrastruktura *No changes*).
  `tofu apply -auto-approve` uloží nový text.

### D. PR a sloučení (vy)

- [ ] **D1.** Commit → push větve `secrets/krok10` → PR → ✅ *Build a testy* → **Merge**.
- [ ] **D2.** CI na `main` → *Nasazení* ✅. Na VM ještě běží **starý** `deploy.sh` – secrets na vstupu ignoruje,
  nasadí jako dřív.

### E. Nový `deploy.sh` na VM (vy / Claude)

- [ ] **E1.** Skript se na VM neaktualizuje sám (krok 8) – nainstalovat ho z `main`:

  ```powershell
  ssh minishop-vm "curl -fsSL https://raw.githubusercontent.com/ftichy/MiniShop/main/deploy/deploy.sh -o /tmp/md && sudo install -m 755 -o root -g root /tmp/md /usr/local/bin/minishop-deploy && rm /tmp/md && grep -c production.env /usr/local/bin/minishop-deploy"
  ```

  Výstup číslo větší než 0 = nová verze.

### F. První nasazení se secrets = rotace hesla RabbitMQ (vy)

- [ ] **F1.** *Actions* → **Deploy** → *Run workflow* → SHA sloučeného commitu (viz `docs/secrets.md`, *Které SHA*).
- [ ] **F2.** Log kroku *Nasazení (deploy.sh na VM)*: `Měním heslo RabbitMQ (uživatel minishop)` → `Hotovo: sha-…`.
- [ ] **F3.** https://minishop-tichy.duckdns.org → stejné heslo jako dřív → objednávka → *Confirmed*
  (Web i sklad se k RabbitMQ přihlásily novým heslem).

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `deploy/production.env` | netajná konfigurace produkce (`COMPOSE_FILE`, `IMAGE_PREFIX`, `DOMAIN`, `BASIC_AUTH*`, `RABBITMQ_USER`) |
| `.github/dependabot.yml` | týdenní kontrola NuGet (skupiny Microsoft/OpenTelemetry/Serilog/testy), GitHub Actions, Docker |
| `docs/secrets.md` | registr secrets: kde leží, kdo je používá, postupy výměny R1–R7 |
| `docs/kroky-cloud/krok-10-secrets.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/deploy.sh` | secrets ze stdin (whitelist + kontrola tvaru), `.env` se pokaždé sestaví (600), rotace hesla RabbitMQ (`rabbitmqctl change_password`, při chybě zpět), funguje i na nové VM bez `.env` |
| `.github/workflows/deploy.yml` | secrets `RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH` → stdin SSH; chybějící secret = chyba hned |
| `infra/bootstrap.sh.tftpl` | bez `.env` a bez startu aplikace – jen složka `/opt/minishop` |
| `infra/compute.tf`, `infra/variables.tf` | pryč `basic_auth_hash`, `basic_auth_user` |
| `infra/outputs.tf`, `infra/terraform.tfvars.example` | další kroky po nové VM (Deploy), poznámka o secrets |
| `deploy/.env.example` | sekce pro VM nahrazena odkazem na `production.env` a `docs/secrets.md` |
| `deploy/set-basic-auth.sh` | **smazán** – heslo se mění secretem (`docs/secrets.md`, R2) |

Mimo repozitář: GitHub secrets `RABBITMQ_PASSWORD`, `BASIC_AUTH_HASH`, zapnuté secret scanning, push protection
a Dependabot; na VM nový `/usr/local/bin/minishop-deploy` a nové heslo RabbitMQ; `terraform.tfvars` bez otisku.

## Rozhodnutí v tomto kroku

- **GitHub environment jako trezor**, ne OCI Vault – pro jednu VM stačí, secrets už tam jsou (krok 8), přístup
  má jen workflow s environmentem `production` (jen z `main` a ručně).
- **Secrets přes stdin SSH**, ne v příkazu ani v souboru na runneru – nejsou v logu, v seznamu procesů ani na disku runneru.
- **`.env` na VM je odvozený** – sestaví se při každém nasazení; ruční úpravy nepřežijí (záměr: jediný zdroj pravdy).
- **Rotace RabbitMQ v `deploy.sh`** – jen když se heslo liší a RabbitMQ běží; při chybě vrátí staré heslo.
- **Heslo RabbitMQ jen písmena a číslice, 24–128 znaků** – je součástí adresy `amqp://`.
- **Netajná konfigurace v gitu** – změna domény nebo vypnutí zámku jde přes PR (review, historie).
- **Nová VM bez aplikace** – OpenTofu nemá secrets aplikace; první start = běžné nasazení.
- **Dependabot se skupinami** – související balíčky v jednom PR (jinak konflikt verzí NU1605).
- **Bez OIDC pro Tailscale** – OAuth secret zůstává (rotace R4); federace identit je možné rozšíření.

## Známá omezení

- **Heslo RabbitMQ je při rotaci krátce vidět v seznamu procesů na VM** – `rabbitmqctl change_password` ho neumí
  přečíst ze vstupu. Na VM mají přístup jen správci (`ubuntu`, `deploy` ≈ root).
- **Rotace = krátký výpadek** – RabbitMQ, Web i sklad se restartují (jako při každém nasazení s jinou konfigurací).
- **`deploy.sh` na VM se neaktualizuje sám** – po změně ho nainstalovat (E1).
- **Kdo může upravovat workflow v `main`, může secrets číst** – chrání to ruleset (`main` jen přes PR) a
  environment `production` (jen větev `main`).
- **Secret scanning pozná jen známé formáty** (tokeny GitHubu, cloudů…), ne obecná hesla – proto gitleaks
  při kontrole historie a registr.
- **Použitý Tailscale auth key zůstává ve state a v metadatech VM** – bezcenný, ale je tam.
- **Rotace OCI klíčů a SSH klíčů je ruční** (postupy R3–R7 v registru), připomínku nic neposílá.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| `git grep -n -i -E "password\|secret" -- deploy infra` | jen názvy proměnných a komentáře, žádné hodnoty |
| gitleaks přes celou historii (viz níže) | `no leaks found` |
| `ssh minishop-vm "sudo ls -l /opt/minishop/.env; sudo head -1 /opt/minishop/.env"` | `-rw------- deploy deploy`, první řádek `# Generuje minishop-deploy…` |
| Log Deploy | `Měním heslo RabbitMQ` jen když se heslo změnilo; hodnoty secrets nikde (jen `***`) |
| GitHub → *Security* | Dependabot alerts a Secret scanning zapnuté |
| GitHub → *Pull requests* (po pondělí) | PR od Dependabota (`deps:`, `ci:`, `docker:`) s CI |
| `tofu plan` | *No changes* |

Kontrola historie (z kořene repozitáře, Docker):

```powershell
docker run --rm -v "${PWD}:/repo:ro" ghcr.io/gitleaks/gitleaks:latest git /repo --redact --no-banner --log-opts="--all"
```

## Vyzkoušejte sami

1. **Rotace hesla do webu.** Podle `docs/secrets.md` R2: nový otisk → secret → *Run workflow* → prohlížeč
   (anonymní okno) chce nové heslo. Pak klidně zpět.
2. **Push protection.** Na pokusné větvi vytvořte soubor s řetězcem, který vypadá jako token GitHubu
   (`ghp_` + 36 znaků – vymyšlený), commit → `git push` → GitHub push odmítne a vypíše, kde secret je.
   Commit pak zrušte (`git reset HEAD~1`) a soubor smažte.
3. **Ochrana vstupu.** Na VM: `printf 'EVIL=1\n' | sudo -u deploy minishop-deploy <SHA>` → *Neznámý klíč na vstupu*, exit 2.
   A `printf 'RABBITMQ_PASSWORD=abc$(id)…\n' | …` → *Chybný RABBITMQ_PASSWORD*.
4. **Konfigurace přes PR.** V `deploy/production.env` dočasně `BASIC_AUTH=off` → PR → po sloučení je web bez hesla.
   Druhým PR zpět. (Na veřejném webu jen na chvíli!)
5. **Dependabot.** *Insights* → *Dependency graph* → *Dependabot* → *Check for updates* – PR dorazí hned.
