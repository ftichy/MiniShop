# Registr secrets MiniShopu

Živý seznam všech tajných hodnot: kde leží, kdo je používá a jak je vyměnit. Vznikl v cloud kroku 10
(`docs/kroky-cloud/krok-10-secrets.md`). **Nový secret = nový řádek tady.** Hodnoty sem nikdy nepatří.

Pravidla:
1. Nikdy v gitu (ani v historii) – hlídá GitHub *secret scanning* + *push protection*.
2. Každý secret jen tam, kde je potřeba (nejmenší oprávnění).
3. Každý musí jít vyměnit bez přestavby serveru – postup je v tabulce níže.

## Secrets aplikace (zdroj pravdy: GitHub → Settings → Environments → `production`)

| Secret | K čemu | Kde ještě leží | Výměna | Kdy |
| --- | --- | --- | --- | --- |
| `RABBITMQ_PASSWORD` | heslo účtu `minishop` v RabbitMQ (Web a sklad se jím přihlašují) | `.env` na VM (600, vlastník `deploy`) – sestaví ho `deploy.sh` | [R1](#r1-heslo-rabbitmq) | při podezření na únik, jinak 1× ročně |
| `BASIC_AUTH_HASH` | bcrypt otisk hesla pro zámek v Caddy (heslo zná jen uživatel) – od účtů kroku 9 jen Aspire dashboard, web je veřejný | `.env` na VM | [R2](#r2-heslo-do-webu-basic-auth) | při podezření, při odchodu člověka, který heslo zná |
| `HEALTHCHECKS_BACKUP_URL` | adresa, kam noční záloha hlásí průběh (healthchecks.io, krok 12) – kdo ji zná, může hlídači „nalhat“ zálohu | `.env` na VM | healthchecks.io → kontrola → *Ping URL* → *Regenerate* → secret → Deploy | při podezření |
| `SMTP_PASSWORD` | heslo aplikace Gmailu účtu `ftichy.gemini@gmail.com` – Web jím posílá e-maily (účty krok 5). Dává přístup k **celé schránce** toho účtu. Volitelný: prázdný = e-maily jen do logu | `.env` na VM | [R8](#r8-heslo-aplikace-gmailu) | při podezření, jinak 1× ročně |
| `INVENTORY_API_KEY` | servisní klíč mezi Webem a skladem (účty krok 7) – kdo ho zná a dostane se do sítě Dockeru, může měnit produkty a zásoby. 64 hex znaků, povinný | `.env` na VM → proměnná `InventoryApi__ApiKey` v kontejnerech `web` a `inventory-api` | [R9](#r9-servisní-klíč-skladu) | při podezření, jinak 1× ročně |

## Secrets nasazení (GitHub → Environments → `production`)

| Secret | K čemu | Kde ještě leží | Výměna | Kdy |
| --- | --- | --- | --- | --- |
| `DEPLOY_SSH_KEY` | soukromý SSH klíč, kterým CD spouští `minishop-deploy` na VM | veřejná část: VM `/home/deploy/.ssh/authorized_keys`, PC `~/.ssh/minishop_deploy.pub` (pro OpenTofu) | [R3](#r3-ssh-klíč-nasazení) | 1× ročně |
| `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET` | OAuth klient Tailscale – runner se připojí jako `tag:ci` | Tailscale admin → *Settings* → *OAuth clients* | [R4](#r4-tailscale-oauth-klient) | 1× ročně |

`GITHUB_TOKEN` (push image do GHCR) vytváří GitHub pro každý běh sám a po něm zanikne – nic se nemění.

## Secrets na PC správce (mimo repozitář)

| Secret | K čemu | Kde | Výměna | Kdy |
| --- | --- | --- | --- | --- |
| API klíč `minishop-automation` | OCI CLI a OpenTofu (jen compartment `minishop`) | `~/.oci/minishop-automation.pem` + `~/.oci/config` | [R5](#r5-oci-api-klíč) | 1× za 90 dní |
| Customer Secret Key `tofu-state` | OpenTofu čte/zapisuje state přes S3 API | `~/.oci/s3-credentials` | [R6](#r6-customer-secret-key-pro-state) | 1× za 90 dní |
| SSH klíč `minishop_oci` (s passphrase) | správcovský přístup `ubuntu@minishop-vm` přes Tailscale | `~/.ssh/minishop_oci` | [R7](#r7-ssh-klíč-správce) | při ztrátě PC |
| Tailscale auth key | připojení NOVÉ VM do Tailscale (cloud-init) | `infra/terraform.tfvars`, state OpenTofu, metadata VM | jednorázový – na každou novou VM nový | po použití je bezcenný |
| Identita VM (instance principal) | VM se přihlásí k bucketu záloh bez klíče (krok 12) | nikde – OCI ji odvozuje z dynamické skupiny `minishop-vm` | není co měnit; práva řídí politika `minishop-backups` | — |

## Netajné, ale citlivé

| Co | Kde | Poznámka |
| --- | --- | --- |
| OpenTofu state | bucket `minishop-tfstate` (soukromý, verzovaný) | obsahuje `user_data` VM (použitý auth key) – nikdy do gitu |
| OCID (compartment, VM…) | `infra/terraform.tfvars` | identifikátory, ne klíče; bez přihlášení k ničemu |
| `DEPLOY_HOST`, `DEPLOY_KNOWN_HOSTS` | GitHub environment *variables* | veřejné údaje VM (adresa v Tailscale, veřejný otisk serveru) |
| Konfigurace produkce | `deploy/production.env` (v gitu) | doména, uživatelé, prefix image – změna přes PR |
| Data Protection klíče, certifikáty | volumes na VM (`web-data`, `caddy-data`) | spravuje je aplikace / Caddy samy (rotují se samy) |
| `guest`/`guest` v `RabbitMqOptions` | kód | výchozí hodnota jen pro lokální vývoj; RabbitMQ pouští `guest` jen z localhostu |

Účty (OCI administrátor, GitHub, Tailscale, DuckDNS) chrání vlastní heslo + MFA – nejsou v tomto registru.
DuckDNS token se nikde neukládá (reserved IP se nemění).

## Postupy výměny

### R1. Heslo RabbitMQ

1. Nové heslo (PowerShell, 48 hexa znaků):

   ```powershell
   $b = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
   ($b | ForEach-Object { $_.ToString('x2') }) -join '' | Set-Clipboard
   ```

2. GitHub → *Settings* → *Environments* → `production` → `RABBITMQ_PASSWORD` → *Update* → vložit → `Set-Clipboard -Value " "`.
3. *Actions* → **Deploy** → *Run workflow* → SHA nasazené verze (viz [Které SHA](#které-sha-nasadit)).
4. V logu kroku *Nasazení*: `Měním heslo RabbitMQ (uživatel minishop)` → `Hotovo`. `deploy.sh` heslo nejdřív
   změní v běžícím RabbitMQ (`rabbitmqctl change_password`), pak restartuje služby s novým. Když nasazení selže,
   vrátí staré heslo i verzi.

### R2. Heslo do webu (basic auth)

Od účtů kroku 9 chrání jen Aspire dashboard (`dashboard.minishop-tichy.duckdns.org`), web je veřejný.

1. Otisk nového hesla: `docker run --rm -it caddy:2 caddy hash-password` (heslo 2×, vypíše `$2a$14$…`).
2. GitHub → `production` → `BASIC_AUTH_HASH` → *Update* → otisk (bez uvozovek).
3. *Actions* → **Deploy** → *Run workflow* → SHA nasazené verze. Caddy se restartuje s novým heslem.

### R3. SSH klíč nasazení

1. PC: `ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\minishop_deploy_new -C "github-actions-deploy"` (bez passphrase).
2. VM: do `/home/deploy/.ssh/authorized_keys` přidat **druhý** řádek
   `restrict,command="/usr/local/bin/minishop-deploy" <obsah .pub>` (staré nechat – nasazení dál funguje).
3. GitHub → `DEPLOY_SSH_KEY` → obsah soukromého klíče → *Run workflow* Deploy → zelené.
4. VM: starý řádek smazat. PC: soukromý klíč smazat, `.pub` přejmenovat na `minishop_deploy.pub` (čte ho OpenTofu).

### R4. Tailscale OAuth klient

1. Tailscale admin → *Settings* → *OAuth clients* → nový (scope *Auth Keys: Write*, tag `tag:ci`).
2. GitHub → `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET` → nové hodnoty → *Run workflow* Deploy → zelené.
3. Starý OAuth klient → *Revoke*.

### R5. OCI API klíč

1. OCI → *Identity* → *Domains* → *Default* → *Users* → `minishop-automation` → *API keys* → *Add API key*
   → *Generate API key pair* → stáhnout soukromý klíč do `~/.oci/` (nový název).
2. `~/.oci/config`: `key_file` a `fingerprint` na nový klíč. Ověření: `tofu plan` → *No changes*.
3. Starý klíč v konzoli → *Delete*, starý `.pem` z PC smazat.

### R6. Customer Secret Key pro state

1. OCI → `minishop-automation` → *Customer secret keys* → *Generate* (max. 2 současně).
2. `~/.oci/s3-credentials` → nové hodnoty. Ověření: `tofu plan` (čte state) → *No changes*.
3. Starý klíč → *Delete*.

### R7. SSH klíč správce

Nový klíč (`ssh-keygen`), veřejnou část přidat na VM do `/home/ubuntu/.ssh/authorized_keys` (přes starý klíč),
`~/.ssh/config` → nový `IdentityFile`; starý řádek z `authorized_keys` smazat. Pro příští VM z OpenTofu:
`ssh_public_key_path` v `terraform.tfvars`.

### R8. Heslo aplikace Gmailu

1. Přihlásit se jako `ftichy.gemini@gmail.com` → https://myaccount.google.com/apppasswords
   (vyžaduje zapnuté dvoufázové ověření) → nové heslo aplikace „MiniShop“.
2. Google ukáže 16 písmen ve skupinách po čtyřech – do secretu **bez mezer**, malými písmeny
   (`deploy.sh` jiný tvar odmítne).
3. GitHub → *Environments* → `production` → `SMTP_PASSWORD` → nová hodnota → *Actions → Deploy → Run workflow*
   (SHA viz níže).
4. Ověření: na webu *Zapomenuté heslo* pro svůj účet → e-mail dorazí.
5. Staré heslo aplikace v Google účtu smazat (do té doby platí obě).

### R9. Servisní klíč skladu

1. Nový klíč – 64 malých hex znaků (náhodných, ne vymyšlených), např. na VM `openssl rand -hex 32`
   nebo v PowerShellu
   `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); -join ($b | ForEach-Object { $_.ToString('x2') })`.
2. GitHub → *Environments* → `production` → `INVENTORY_API_KEY` → nová hodnota → *Actions → Deploy → Run workflow*
   (SHA viz níže). Compose znovu vytvoří oba kontejnery – po pár sekund může stránka Produkty hlásit „sklad nedostupný“.
3. Ověření: stránka Produkty se načte; v logu skladu není „Požadavek na sklad se špatným servisním klíčem“.

### Které SHA nasadit

Celý SHA posledního commitu na `main`, ze kterého CI publikovalo image (commity jen s dokumentací image nemají):
GitHub → *Actions* → workflow **CI** → poslední zelený běh na `main` s jobem *Nasazení* → SHA v detailu běhu.
Nebo z PC:

```powershell
git fetch; git log origin/main --first-parent -1 --format=%H -- . ':!docs' ':!*.md'
```
