# Cloud krok 9: Infrastruktura jako kód (OpenTofu)

V kroku 4 jste síť a VM naklikal v konzoli a server připravil ručně příkazy přes SSH (kroky 4, 5, 8).
Funguje to, ale nikde to není zapsané celé – když VM zmizí, musíte projít tři návody a doufat, že nic nevynecháte.
Teď celou infrastrukturu popíšete v souborech ve složce `infra/` a **OpenTofu** ji podle nich postaví.
Na konci ruční VM i síť smažete a `tofu apply` je postaví znovu – i s běžícím MiniShopem.

## Jak to funguje

Přirovnání: krok 4 byla stavba domu „od oka“. Teď máte **stavební plány** (`*.tf`), **stavbyvedoucího**
(OpenTofu), který porovná plány se skutečností a dostaví jen rozdíl, a **kartotéku** (state), kde má zapsáno,
která zeď je která. **cloud-init** je pracovní četa, která při prvním otevření dveří nastěhuje nábytek.

```
 PC: infra/*.tf ──► tofu plan / apply ──► OCI API (jako minishop-automation, jen compartment minishop)
        │                 │
        │                 └──► state: bucket minishop-tfstate (Object Storage, S3 API, verzovaný)
        │
        └─ cloud-init.yaml.tftpl + bootstrap.sh.tftpl + deploy/deploy.sh ──► user_data VM
                                                                              │ 1. start VM
                                                                              ▼
            VM: iptables 80/443 → Docker → Tailscale (auth key) → uživatel deploy → /opt/minishop/.env
                → docker compose up (image :latest) → MiniShop běží
                                                                              │ 2. první nasazení z CI
                                                                              ▼
            GitHub Actions ──Tailscale/SSH──► minishop-deploy <sha> → IMAGE_TAG=sha-…
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **IaC** | infrastruktura popsaná textem, verzovaná v gitu | složka `infra/` |
| **Provider** | plugin, který umí mluvit s API jednoho cloudu | `oracle/oci` 9.x (verze zamčená v `.terraform.lock.hcl`) |
| **Resource** | věc, kterou OpenTofu vytváří a spravuje | VCN, subnet, gateway, route table, security list, VM, reserved IP |
| **Data source** | věc, kterou jen čte | nejnovější obraz Ubuntu, availability domains, privátní IP VM |
| **State** | kartotéka: resource v kódu ↔ OCID v cloudu | `minishop/terraform.tfstate` v bucketu `minishop-tfstate` |
| **plan / apply** | ukázat rozdíl / provést ho | `+` vytvořit, `~` změnit, `-` smazat, `-/+` smazat a vytvořit znovu |
| **import** | převzít existující věc do state (nic se v cloudu nemění) | reserved IP `minishop-ip` z kroku 5 |
| **cloud-init** | skript, který VM provede jednou při prvním startu | `infra/cloud-init.yaml.tftpl` → `bootstrap.sh.tftpl` |

**Deklarativně, ne příkazy.** V kódu není „vytvoř VM“, ale „má existovat VM s 2 OCPU“. OpenTofu se podívá do
state a do cloudu: když VM chybí, vytvoří ji, a když existuje, nedělá nic. Když ji někdo v konzoli změní, `plan`
ukáže rozdíl (**drift**) a `apply` ji vrátí podle kódu.

**Proč state v cloudu, a ne na PC:** bez state by OpenTofu nevěděl, že VCN `ocid1.vcn…` je ta jeho, a vytvořil
by druhou. Na PC by se state dal ztratit (nový disk) a nedal by se sdílet (budoucí workflow). Bucket má zapnuté
**verzování** – každý `apply` uloží novou verzi a starou jde obnovit.

**Proč VM znovu, a ne import:** cílem je ověřit, že kód umí postavit server od nuly. Importovaná VM by tenhle
test nesplnila – byla by pořád ta ručně připravená. Síť z průvodce (s privátním subnetem, NAT a Service Gateway,
které nepoužíváme) taky smažeme a postavíme jen to, co MiniShop potřebuje. Importuje se **jen reserved IP** –
na ni ukazuje DNS, nová adresa by znamenala změnu v DuckDNS.

## Návod krok za krokem

Kroky v konzoli OCI děláte vy (compartment **`minishop`**, region **Frankfurt**). Hodnoty 📝 si poznamenejte
mimo repozitář. Příkazy `tofu` spouštějte v PowerShellu ve složce `infra/`.

### A. OpenTofu na PC (vy)

- [ ] **A1.** `winget install OpenTofu.Tofu` → nový terminál → `tofu version` (1.13 nebo novější).

### B. Úložiště pro state (vy, konzole OCI)

- [ ] **B1.** ☰ → *Storage* → *Buckets* → compartment `minishop` → **Create Bucket**: Name **`minishop-tfstate`**,
  *Default Storage Tier* Standard, **Enable Object Versioning** ✅, ostatní výchozí → *Create*.
  Bucket je soukromý (*Visibility: Private*) – zkontrolujte. (Always Free: 20 GB, state má pár kB.)
- [ ] **B2.** Přístupový klíč pro S3 API: ☰ → *Identity & Security* → *Domains* → *Default* → *User management* →
  **minishop-automation** → záložka *Customer secret keys* → **Generate secret key**, Name `tofu-state` →
  zkopírujte **Secret** (ukáže se jen teď!) → *Close* → v tabulce zkopírujte **Access key**.
- [ ] **B3.** Soubor `C:\Users\<vy>\.oci\s3-credentials` (bez přípony, mimo repozitář):

  ```ini
  [default]
  aws_access_key_id = <Access key>
  aws_secret_access_key = <Secret>
  ```

  Formát je z AWS, protože OCI Object Storage umí i S3 API a OpenTofu ukládá state přes backend `s3`.
- [ ] **B4.** V `infra/`: `Copy-Item backend.hcl.example backend.hcl` a doplnit namespace (`oci os ns get`,
  krok 3 G1) a cestu k `s3-credentials` (s lomítky `/`).

### C. Vstupy (vy, dokud běží stará VM!)

- [ ] **C1.** `Copy-Item terraform.tfvars.example terraform.tfvars` a doplnit `compartment_ocid`.
- [ ] **C2.** Otisk hesla ze staré VM (heslo do MiniShopu tak zůstane stejné):

  ```powershell
  ssh minishop-vm "sudo -u deploy grep BASIC_AUTH_HASH /opt/minishop/.env"
  ```

  Do `basic_auth_hash` vložit jen `$2a$14$…` – bez `BASIC_AUTH_HASH=` a bez jednoduchých uvozovek.
- [ ] **C3.** OCID reserved IP: ☰ → *Networking* → *IP management* → *Reserved public IPs* → `minishop-ip` → OCID. 📝

### D. Inicializace a převzetí reserved IP (vy)

- [ ] **D1.** `tofu init "-backend-config=backend.hcl"` → *OpenTofu has been successfully initialized!*
  Uvozovky jsou nutné: Windows PowerShell rozdělí argument začínající `-`, který obsahuje tečku, na dva
  (`-backend-config=backend` a `.hcl`) a OpenTofu pak jen vypíše nápovědu.
  (stáhne provider do `.terraform/`, připojí se k bucketu).
- [ ] **D2.** `tofu import oci_core_public_ip.reserved <OCID z C3>` → *Import successful!*
  Zástupná hodnota `tskey-auth-…` z vzoru stačí – při importu se auth key nepoužije (skutečný až v F1).
  V bucketu teď leží `minishop/terraform.tfstate`. `tofu state list` → `oci_core_public_ip.reserved`.
  Import nic v cloudu nemění – jen zapíše do kartotéky „tahle IP je moje“.

### E. Smazání ruční infrastruktury (vy)

Od teď je MiniShop nedostupný, než doběhne bod G.

- [ ] **E1.** Tailscale admin → *Machines* → `minishop-vm` → ⋯ → **Remove**. Jinak by nová VM dostala jméno
  `minishop-vm-1` a adresa `100.126.167.103` by zůstala obsazená.
- [ ] **E2.** OCI → *Compute* → *Instances* → `minishop-vm` → *Actions* → **Terminate** → ✅ *Permanently delete
  the attached boot volume* → *Terminate instance*. Počkat na *Terminated*.
- [ ] **E3.** *Networking* → *Virtual cloud networks* → `minishop-vcn` → **Delete** → konzole vypíše vše uvnitř
  (subnety, gateway, route tables, security list) → **Delete all**.
- [ ] **E4.** Kontrola: *IP management* → *Reserved public IPs* → `minishop-ip` dál existuje, *Assigned: No*.

### F. Stavba kódem (vy)

- [ ] **F1.** Tailscale admin → *Settings* → *Keys* → **Generate auth key**: *Reusable* ❌, *Ephemeral* ❌,
  *Pre-approved* ✅, *Tags* ✅ `tag:minishop-vm`, *Expiration* 1 day → zkopírovat `tskey-auth-…` do `terraform.tfvars`.
- [ ] **F2.** `tofu plan` – přečtěte si ho. Očekávané: **`Plan: 6 to add, 1 to change, 0 to destroy.`**
  - `+` VCN, Internet Gateway, route table, security list, subnet, instance,
  - `~` `oci_core_public_ip.reserved` (`private_ip_id` → `(known after apply)` = přišpendlí se k nové VM).
  - ⚠️ Kdyby plán chtěl cokoli **zničit** (`destroy`), nepokračujte a ozvěte se.
- [ ] **F3.** `tofu apply` → zopakuje plán → `yes`. Za 1–3 minuty *Apply complete!* a výstupy (`public_ip`
  = `141.144.255.238`, `next_steps`).
  **`400-QuotaExceeded … standard-a1-core-regional-count … by 2`:** kvóta z kroku 3 neměla regionální počítadla A1.
  Do `minishop-quota` (kořenový compartment → *Quota policies*) přidat dva řádky `…-regional-count` (viz krok 3, H2)
  a `tofu apply` znovu – síť už existuje, plán bude `1 to add, 1 to change`.
  **Out of host capacity:** v `terraform.tfvars` `availability_domain_index = 1` (nebo 2) a znovu `tofu apply`.

### G. Kontrola nové VM (vy / Claude)

- [ ] **G1.** Počkat 5–8 minut (aktualizace balíčků, Docker, stažení image). Tailscale admin → *Machines* →
  objeví se `minishop-vm` s `tag:minishop-vm` → ⋯ → **Edit machine IPv4** → `100.126.167.103` → *Update*.
  Díky tomu se nemění `~/.ssh/config` ani `DEPLOY_HOST` v GitHubu.
- [ ] **G2.** Nový server = nový otisk SSH. Starý z `known_hosts` smazat a připojit se:

  ```powershell
  ssh-keygen -R 100.126.167.103
  ssh minishop-vm "cloud-init status; sudo tail -n 15 /var/log/minishop-bootstrap.log"
  ```

  Na dotaz na otisk `yes` – spojení vede přes Tailscale, kam se dostane jen uzel ověřený vaším auth key.
  Očekávané: `status: done` a na konci `=== … Hotovo ===` s tabulkou tří služeb (`healthy`) a `caddy`.
- [ ] **G3.** https://minishop-tichy.duckdns.org → heslo jako dřív → MiniShop. Objednávky jsou prázdné (nová DB),
  produkty ze seedu. Caddy si při prvním požadavku vyžádal nový certifikát (může to trvat ~10 s).
- [ ] **G4.** GitHub → *Settings* → *Environments* → `production` → proměnná **`DEPLOY_KNOWN_HOSTS`** = výstup

  ```powershell
  ssh minishop-vm "ssh-keyscan -t ed25519 100.126.167.103 2>/dev/null"
  ```

  (jeden řádek `100.126.167.103 ssh-ed25519 AAAA…`). Spouští se **na VM** – `ssh-keyscan` z Windows je starší
  a s Ubuntu 24.04 se nedomluví (`choose_kex: unsupported KEX method sntrup761x25519-sha512`).

### H. PR a první nasazení na novou VM (vy)

- [ ] **H1.** Commit → push větve `infra/krok9-opentofu` → PR → ✅ *Build a testy* → **Merge**.
- [ ] **H2.** CI na `main` (změna `infra/` není jen dokumentace) → *Image …* → **Nasazení** → zelené.
  Na VM `IMAGE_TAG` je teď `sha-…` místo `latest`. Tím je ověřený celý řetěz: kód → OpenTofu → VM → CD.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `infra/versions.tf` | verze OpenTofu a provideru `oracle/oci`, backend `s3` (bucket `minishop-tfstate`), provider přes `~/.oci/config` |
| `infra/variables.tf` | vstupy: compartment, tvar VM, cesty k veřejným SSH klíčům, Tailscale auth key, doména, basic auth (s kontrolou formátu otisku) |
| `infra/network.tf` | VCN `10.0.0.0/16`, Internet Gateway, route table, security list (80, 443, ICMP; bez 22), veřejný subnet `10.0.0.0/24` |
| `infra/compute.tf` | obraz Ubuntu 24.04 aarch64, VM A1 (bez ephemeral IP, `ignore_changes` obrazu a metadat), reserved IP (`prevent_destroy`) |
| `infra/outputs.tf` | veřejná IP, OCID VM, použitý obraz, další kroky |
| `infra/cloud-init.yaml.tftpl` | cloud-config: aktualizace balíčků, timeout zámku apt, `minishop-deploy`, spuštění bootstrapu |
| `infra/bootstrap.sh.tftpl` | příprava VM: iptables, Docker, Tailscale, uživatel `deploy`, `/opt/minishop` + `.env` (heslo RabbitMQ generuje VM), první `compose up` |
| `infra/terraform.tfvars.example`, `infra/backend.hcl.example` | vzory vstupů a nastavení state |
| `infra/.terraform.lock.hcl` | zámek verze a otisků provideru (patří do gitu) |
| `.gitattributes` | `*.sh`, `*.tftpl` vždy s LF |
| `docs/kroky-cloud/krok-09-iac.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `.gitignore` | `.terraform/`, `*.tfstate*`, `*.tfplan`, `*.tfvars` (kromě `*.example`), `infra/backend.hcl` |

Mimo repozitář: bucket `minishop-tfstate`, Customer Secret Key `tofu-state` uživatele `minishop-automation`,
na PC `~/.oci/s3-credentials`, `infra/backend.hcl`, `infra/terraform.tfvars`; nová VM a síť (spravuje OpenTofu),
v Tailscale nový uzel `minishop-vm`, v GitHubu nová hodnota `DEPLOY_KNOWN_HOSTS`.

## Rozhodnutí v tomto kroku

- **VM a síť od nuly, import jen reserved IP** – test, že kód umí server postavit; síť bez nepoužívaných částí
  z průvodce. Reserved IP má `prevent_destroy` – `tofu destroy` ji nesmaže (DNS na ni ukazuje).
- **`ignore_changes` u obrazu a metadat VM** – nový obraz Ubuntu nebo úprava cloud-init by jinak znamenaly
  `-/+` (novou VM = ztrátu dat). Nová VM je vědomé rozhodnutí: `tofu apply -replace=oci_core_instance.vm`.
- **cloud-init místo Ansible** – jeden běh při startu bez dalšího nástroje. Server se po startu nemění ručně
  (vše další dělá CD); kdo potřebuje změnu, postaví novou VM.
- **`deploy.sh` jde na VM z repozitáře přes OpenTofu** (`filebase64`) – verze odpovídá commitu infrastruktury.
- **První start s image `latest`** – bootstrap nezná SHA s hotovými image (commity jen s dokumentací image nemají).
  Přesnou verzi `sha-…` nastaví první běh CD.
- **Heslo RabbitMQ vytvoří VM** (`openssl rand`) – není ve state ani v metadatech. Otisk basic auth a auth key
  ve state jsou: auth key je jednorázový (po použití bezcenný), otisk není heslo. Pořádně krok 10.
- **State v OCI Object Storage přes S3 API** – zdarma, verzovaný, soukromý; přístup jen klíčem technického uživatele.
- **Bez zamykání state** (`use_lockfile`) – OpenTofu spouští jen jeden člověk; podporu podmíněných zápisů v S3 API
  OCI jsme neověřovali.
- **Bez workflow `infra.yml`** (`tofu plan` v PR) – potřeboval by v GitHubu klíč s právem měnit infrastrukturu.
  Zvážit po kroku 10 (OIDC místo trvalých klíčů).
- **OpenTofu nativně (winget), ne v Dockeru** – čte `~/.oci/config` i SSH klíče přímo z PC, cesty se nepřekládají.
- **`.gitattributes` s LF** – skripty čte OpenTofu z disku Windows a posílá na Linux; s `core.autocrlf=true`
  by měly CRLF a bash by na VM spadl.

## Známá omezení

- **Nová VM = prázdná data** (objednávky, fronty RabbitMQ, certifikát Caddy). Zálohy a obnova: krok 12.
  Let's Encrypt vydá pro stejnou doménu max. 5 certifikátů za týden – VM často nepřestavovat.
- **Ruční kroky po nové VM zůstávají:** nový Tailscale auth key, IP v Tailscale adminu, `DEPLOY_KNOWN_HOSTS`.
- **`user_data` vidí každý s přístupem do compartmentu** (metadata VM) a leží ve state – obsahuje otisk hesla
  a (použitý) auth key.
- **Změna cloud-init se na běžící VM neprojeví** (`ignore_changes`) – jen s `-replace`.
- **Drift se hledá ručně** – `tofu plan` nikdo nespouští automaticky.
- **Mimo OpenTofu:** compartment, IAM, kvóta a budget (krok 3), bucket se state, DuckDNS, Tailscale (ACL, klíče),
  GitHub (environment, ruleset).
- **Bootstrap bere `compose.yaml` z `main`**, ne z konkrétního commitu (do prvního nasazení CD).
- **`package_upgrade` může chtít restart** (nové jádro) – cloud-init ho neprovede; `sudo reboot` ručně, když
  `ssh` hlásí *System restart required*.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| `tofu plan` (po apply) | `No changes. Your infrastructure matches the configuration.` |
| `tofu state list` | 7 resources + data sources (`data.oci_core_images.ubuntu`, …) |
| `tofu output public_ip` | `"141.144.255.238"` |
| OCI → *Buckets* → `minishop-tfstate` → *Objects* | `minishop/terraform.tfstate`, u něj starší verze (*Show versions*) |
| OCI → *Virtual cloud networks* | jen `minishop-vcn` s jedním subnetem `minishop-public`, bez NAT/Service Gateway |
| `ssh minishop-vm "sudo iptables -L INPUT -n --line-numbers"` | 80 a 443 `ACCEPT` před `REJECT`, řetězec `ts-input` |
| `ssh minishop-vm "sudo -u deploy cat /home/deploy/.ssh/authorized_keys"` | `restrict,command="/usr/local/bin/minishop-deploy" ssh-ed25519 … github-actions-deploy` |
| https://minishop-tichy.duckdns.org | zámek → MiniShop, objednávka → *Confirmed* |
| Actions → běh na `main` | *Nasazení na VM* ✅ na nové VM |

## Vyzkoušejte sami

1. **Drift.** V konzoli přidejte do `minishop-public-sl` pravidlo (např. TCP 8080 z `0.0.0.0/0`). `tofu plan` ukáže
   `~ oci_core_security_list.public` a pravidlo navíc se znaménkem `-`. `tofu apply` → pravidlo zmizí. Kód je pravda.
2. **Nouzové SSH kódem.** V `network.tf` přidejte ingress pravidlo pro TCP 22 ze `<vaše IP>/32`, `tofu plan` → jediná
   změna `~` u security listu. Neaplikujte (nebo aplikujte a pak vraťte) – takhle by vypadal PR „dočasně otevřít 22“.
3. **Pojistka reserved IP.** `tofu plan -destroy` → chyba *Instance cannot be destroyed* u `oci_core_public_ip.reserved`
   – nic se nesmaže, ani ostatní. (Plán zničení celé infrastruktury jinak ukáže 7 položek.)
4. **Co je ve state.** `tofu show` – všimněte si `user_data` a `(sensitive value)` u auth key. Proto je state
   soukromý a nikdy v gitu.
5. **Historie state.** V bucketu u `terraform.tfstate` *Show versions* – každý `apply` = nová verze. Kdyby se state
   poškodil, obnovíte předchozí verzi (*Restore*).
