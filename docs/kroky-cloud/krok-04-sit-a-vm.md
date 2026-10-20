# Cloud krok 4: Síť a VM ručně, první nasazení

Po kroku 3 máte v OCI bezpečný compartment `minishop`, ale nic v něm neběží. Teď v něm ručně v konzoli postavíte
síť a jeden virtuální server (VM), nainstalujete na něj Docker a nasadíte MiniShop stejným `deploy/compose.yaml`
jako v kroku 2 na PC. Na konci MiniShop poprvé běží v cloudu na veřejné IP adrese.

Všechno je tu záměrně „ručně a po staru“ – image se přenáší jako soubor, konfigurace se píše na serveru.
Kroky 7–9 tohle postupně nahradí registry, automatickým nasazením a kódem (OpenTofu). Kdo si to jednou proklikal,
bude pak v kódu rozumět každému řádku.

## Jak to funguje

Přirovnání: v kroku 3 jste si pronajal místnost v budově a dostal klíče. Teď do ní **přivedete internet** (síť),
**postavíte kancelář** (VM), **nastavíte vrátné** (firewally) a **nastěhujete nábytek** (kontejnery).

| Kancelář | OCI / Linux | MiniShop |
| --- | --- | --- |
| Areál firmy s vlastními cestami | **VCN** (Virtual Cloud Network) – soukromá síť | `minishop-vcn`, 10.0.0.0/16 |
| Část areálu u silnice | **veřejný subnet** | `public subnet-minishop-vcn` |
| Brána na silnici | **Internet Gateway** + směrovací tabulka | vytvoří průvodce |
| Vrátný u brány areálu | **security list** (cloudový firewall) | 22 a 80 jen z vaší IP |
| Vrátný přímo u dveří kanceláře | **iptables** na VM (firewall OS) | OCI Ubuntu pouští jen 22 |
| Kancelář | **VM** (compute instance) | `minishop-vm`, `VM.Standard.A1.Flex`, 2 OCPU / 12 GB |
| Klíč od dveří | **SSH klíč** (ne heslo) | `~/.ssh/minishop_oci` |
| Adresa na dopisy | **veřejná IP** (ephemeral) | `http://<IP>` |

```
  váš PC (IP 1.2.3.4)                          Oracle Cloud – Frankfurt, compartment minishop
  ───────────────────                ┌────────────────────── VCN minishop-vcn 10.0.0.0/16 ─────────────────────┐
                                     │                                                                         │
  prohlížeč ── http :80 ──┐          │   ┌── veřejný subnet 10.0.0.0/24 ──────────────────────────────────┐    │
                          │          │   │                                                                │    │
  ssh ──────── tcp  :22 ──┼──► Internet ─►[security list]──► VM minishop-vm (Ubuntu 24.04 ARM)            │    │
                          │   Gateway│   │  ① cloudový       ┌──────────────────────────────────────────┐ │    │
                          │          │   │     vrátný        │ ② iptables (vrátný OS)                   │ │    │
                          │          │   │  22, 80 jen       │ Docker Compose:                          │ │    │
                          │          │   │  z 1.2.3.4/32     │   web :80→8080 ─► inventory-api ─► rabbit│ │    │
                          │          │   │                   └──────────────────────────────────────────┘ │    │
                          │          │   └────────────────────────────────────────────────────────────────┘    │
                          │          └─────────────────────────────────────────────────────────────────────────┘
```

**Dva firewally za sebou.** Paket musí projít oběma vrátnými. Security list (①) je mimo VM v síti OCI,
iptables (②) běží v Linuxu na VM. Oracle v obrazech Ubuntu nastavuje iptables přísně: pouští dovnitř jen SSH,
všechno ostatní odmítne – i když security list port otevře. To je klasická past: „v konzoli mám port 80 povolený,
a stejně to nejde“. Proto otevíráte port na **obou** místech.

**SSH klíč místo hesla.** Stejný princip jako API klíč v kroku 3: veřejnou část dostane VM při vytvoření,
soukromá zůstane na PC. Přihlášení heslem je na OCI obrazech vypnuté – hádání hesel z internetu nemá šanci.

**Proč image nestavíme na VM?** Šlo by to (`docker compose up --build` na VM), ale na server by musel celý zdrojový
kód a .NET SDK. Hotovou image postavíte na PC a přenesete jako soubor (`docker save` → `scp` → `docker load`).
Stejný princip – „postav jednou, nasaď hotové“ – použije v kroku 7 registry GHCR.

**Proč `--platform linux/arm64`?** VM Ampere A1 má procesor ARM, váš PC x86. Image pro x86 by na ARM nespustila.
Dockerfile z kroku 1 to umí: SDK běží na PC nativně a jen výsledek se přeloží pro ARM (`-a $TARGETARCH`) – rychle,
bez emulace.

## Návod krok za krokem

Odškrtávejte si. Kroky v konzoli děláte pod svým administrátorským účtem, vždy v compartmentu **`minishop`**
(rozbalovací seznam *Compartment* vlevo nebo ve formuláři) a v regionu **Germany Central (Frankfurt)**.
Hodnoty 📝 si poznamenejte mimo repozitář.

### A. SSH klíč (PowerShell na PC)

- [ ] **A1.** Vytvořte složku a klíč:

  ```powershell
  New-Item -ItemType Directory -Force "$env:USERPROFILE\.ssh"
  ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\minishop_oci" -C "minishop-oci"
  ```

  Na dotaz *passphrase* zadejte heslo ke klíči (doporučeno – kdyby někdo soubor ukradl, bez hesla ho nepoužije),
  nebo jen Enter (bez hesla, pohodlnější). Vzniknou dva soubory:
  `minishop_oci` (**soukromý**, nikomu) a `minishop_oci.pub` (**veřejný**, ten nahrajete do OCI).
- [ ] **A2.** Zjistěte svou veřejnou IP adresu (pro security list): 📝

  ```powershell
  Invoke-RestMethod https://api.ipify.org
  ```

  Potřebujete **IPv4** (čtyři čísla s tečkami, např. `94.112.x.x`). Služby jako `ifconfig.me` mohou vrátit IPv6
  (`2a02:…`), a ta by v security listu nefungovala – síť MiniShopu má IPv6 vypnuté.

### B. Síť – VCN průvodcem

- [ ] **B1.** ☰ → *Networking* → *Virtual cloud networks*. Pod vyhledáváním musí být filtr
  *Applied filters: Compartment **minishop*** (jinak ho přepněte). Seznam je zatím prázdný (*No items to display*).
  ⚠️ **Neklikejte na *Create VCN*** – vytvoří jen holou síť bez brány do internetu a subnetů.
  Klikněte na **Actions ▾** → **Start VCN Wizard**. (Kdyby tam nebyl: ☰ → *Networking* → *Overview* →
  *Start VCN Wizard*, nebo do vyhledávání nahoře napsat „VCN Wizard“.)
- [ ] **B2.** V okně průvodce vyberte **Create VCN with Internet Connectivity** → **Start VCN Wizard**.
  Formulář *Basic information*:

  | Pole | Hodnota |
  | --- | --- |
  | VCN name | `minishop-vcn` |
  | Compartment | `minishop` |
  | VCN IPv4 CIDR blocks | `10.0.0.0/16` (výchozí – nechat) |
  | Enable IPv6 / IPv6 prefixes | vypnuto (nechat) |
  | Use DNS hostnames in this VCN | zapnuto (nechat) |
  | Public subnet – IPv4 CIDR block | `10.0.0.0/24` (výchozí) |
  | Private subnet – IPv4 CIDR block | `10.0.1.0/24` (výchozí) |

  → **Next** → stránka *Review and create* ukáže, co vznikne → **Create** → po chvíli **View VCN**.

  `10.0.0.0/16` = soukromé adresy 10.0.x.x (65 536 adres) jen uvnitř vaší sítě; `/24` = 256 adres 10.0.0.x
  pro veřejný subnet. Z internetu tyto adresy vidět nejsou – VM dostane navíc veřejnou IP (bod C4).
  Průvodce vytvoří VCN, veřejný a privátní subnet, Internet Gateway, NAT Gateway, Service Gateway
  a směrovací tabulky. Všechno zdarma. Privátní subnet teď nepoužijeme.
- [ ] **B3.** Upravte vrátného veřejného subnetu: detail `minishop-vcn` → *Security* (nebo *Security Lists*) →
  **Default Security List for minishop-vcn** → *Security rules* / *Ingress Rules*:
  - pravidlo pro **TCP port 22** se zdrojem `0.0.0.0/0` → *Edit* → Source CIDR: **`<vaše IP>/32`**;
  - **Add Ingress Rules**: Source CIDR **`<vaše IP>/32`**, IP Protocol **TCP**, Source Port Range prázdné,
    Destination Port Range **`80`**, popis „MiniShop HTTP – dočasně jen moje IP (do kroku 5)“.
  - Pravidla pro ICMP nechte být (OCI je potřebuje kvůli MTU).

  `/32` = právě jedna adresa. Aplikace tak do kroku 5 (přihlášení a HTTPS) není vidět celému internetu.

### C. VM

- [ ] **C1.** ☰ → *Compute* → *Instances* → compartment `minishop` → **Create instance**.
- [ ] **C2.** Name: `minishop-vm`, Compartment: `minishop`. *Placement*: nechte AD-1 (při nedostatku kapacity
  zkuste AD-2, AD-3).
- [ ] **C3.** *Image and shape* → **Change image** → **Canonical Ubuntu** → **24.04** (ne *Minimal*) → *Select*.
  **Change shape** → *Virtual machine* → **Ampere** → `VM.Standard.A1.Flex` → OCPU **2**, Memory **12 GB**.
  Musí u toho být štítek **Always Free-eligible**. Když ho nevidíte, nic nevytvářejte a ozvěte se.
- [ ] **C4.** *Networking*: VCN `minishop-vcn`, subnet **public subnet-minishop-vcn**,
  **Automatically assign public IPv4 address: zapnuto**.
  Ostatní nechat: *Private IPv4 address* → *Automatically assign*, IPv6 vypnuté (žluté varování, že VCN IPv6
  nepodporuje, je v pořádku), *Advanced options* nerozbalovat.
- [ ] **C5.** *Add SSH keys* (na stejné stránce *Networking*, dole) – výchozí volba je *Generate a key pair for me*,
  **přepněte ji** na **Upload public key file (.pub)** → `C:\Users\<vy>\.ssh\minishop_oci.pub`
  (nebo *Paste public keys* a vložit obsah `.pub`). ⚠️ Nikdy ne soubor bez `.pub`.
- [ ] **C6.** *Storage* / *Boot volume*: nechte výchozí velikost (~47 GB; zdarma je celkem 200 GB). Na *Review*
  zkontrolujte *Shape build: 2 OCPU, 12 GB* → **Create**.
  *View estimated cost* ukáže u boot volume ~46 Kč/měsíc – je to **ceníková cena bez Always Free** (stránka
  sama píše „does not reflect any tier unit pricing“). Compute A1 tam není vůbec. Skutečná útrata je 0;
  ověříte druhý den v *Cost Analysis*, a kdyby nebyla, ozve se budget z kroku 3.
- [ ] **C7.** Počkejte na stav **Running** (1–3 minuty). Na detailu VM zkopírujte **Public IP address**. 📝

**Chyba *Out of host capacity*:** ve Frankfurtu teď nejsou volné ARM servery. Zkuste jinou availability domain
(C2), menší tvar (1 OCPU / 6 GB – pro MiniShop stačí), nebo to zkuste znovu za pár hodin. S Pay As You Go
(krok 3, bod I) se to stává méně.

- [ ] **C8.** Ověření pod technickým uživatelem (PowerShell, funkce `oci` z kroku 3):

  ```powershell
  oci compute instance list --compartment-id <OCID minishop> | Select-String '"display-name"|"lifecycle-state"|"shape"'
  ```

  → `minishop-vm`, `RUNNING`, `VM.Standard.A1.Flex`. Technický uživatel vidí VM, kterou jste vytvořil vy –
  práva jsou na compartment, ne na autora.

### D. Připojení přes SSH

- [ ] **D1.** Zkratka, ať nemusíte psát IP a cestu ke klíči: vytvořte soubor `C:\Users\<vy>\.ssh\config`
  (bez přípony) s obsahem:

  ```
  Host minishop-vm
      HostName <veřejná IP z C7>
      User ubuntu
      IdentityFile ~/.ssh/minishop_oci
  ```

- [ ] **D2.** `ssh minishop-vm` → při prvním připojení otázka na otisk serveru (*fingerprint*) → `yes`.
  Jste na serveru, výzva vypadá jako `ubuntu@minishop-vm:~$`. Odhlášení: `exit`.

  Nejde to? `Connection timed out` = security list (špatná IP v B3, nebo se vaše IP mezitím změnila – A2).
  `Permission denied (publickey)` = jiný klíč než v C5.

### E. Příprava serveru (příkazy na VM přes SSH)

- [ ] **E1.** Aktualizace systému:

  ```bash
  sudo apt update && sudo apt upgrade -y
  ```

  Když se objeví fialové okno *Which services should be restarted?*, potvrďte Enter. Pokud na konci píše
  *System restart required*, `sudo reboot`, chvíli počkejte a znovu `ssh minishop-vm`.
- [ ] **E2.** **Druhý vrátný – iptables.** Podívejte se na pravidla:

  ```bash
  sudo iptables -L INPUT -n --line-numbers
  ```

  Na konci je `REJECT … reject-with icmp-host-prohibited` – co není výš povoleno, odmítne. Výš je jen port 22.
  Vložte povolení pro 80 a 443 **před** REJECT – na jeho číslo řádku (u obrazu 2026.09 je to **5**:
  1 navázaná spojení, 2 ICMP, 3 loopback, 4 SSH, 5 REJECT):

  ```bash
  sudo iptables -I INPUT 5 -m state --state NEW -p tcp --dport 80 -j ACCEPT
  sudo iptables -I INPUT 5 -m state --state NEW -p tcp --dport 443 -j ACCEPT
  sudo netfilter-persistent save
  ```

  `netfilter-persistent save` uloží pravidla do `/etc/iptables/rules.v4`, aby přežila restart.
  ⚠️ Udělejte to **před instalací Dockeru** (E3) – jinak by se do souboru uložila i dočasná pravidla Dockeru
  a po restartu by se s ním prala. Port 443 povolujeme už teď pro krok 5; security list ho zatím nepustí.
- [ ] **E3.** Docker Engine + Compose plugin oficiálním instalačním skriptem a práva pro uživatele `ubuntu`:

  ```bash
  curl -fsSL https://get.docker.com -o get-docker.sh
  sudo sh get-docker.sh
  sudo usermod -aG docker ubuntu
  exit
  ```

  Po `exit` se znovu připojte (`ssh minishop-vm`) – členství ve skupině `docker` platí až od nového přihlášení.
  Ověření: `docker version` (Server: `linux/arm64`) a `docker compose version`.

### F. Image pro ARM a přenos na VM (PowerShell na PC, v kořeni repozitáře)

- [ ] **F1.** Sestavení obou image pro ARM, se štítkem `arm64` (ne `local` – ten patří image pro váš PC):

  ```powershell
  docker buildx build --platform linux/arm64 -f src/MiniShop.Web/Dockerfile -t minishop-web:arm64 --load .
  docker buildx build --platform linux/arm64 -f src/MiniShop.Inventory.Api/Dockerfile -t minishop-inventory-api:arm64 --load .
  ```

- [ ] **F2.** Uložení do souboru a přenos:

  ```powershell
  docker save -o "$env:TEMP\minishop-images.tar" minishop-web:arm64 minishop-inventory-api:arm64
  scp "$env:TEMP\minishop-images.tar" minishop-vm:
  ```

  (Dvojtečka na konci = domovská složka na VM.) Soubor má ~100 MB – obě image sdílí vrstvy .NET runtime
  a `docker save` je uloží jen jednou. Přenos trvá desítky sekund.
  Proč ne rovnou `docker save | ssh … docker load`: Windows PowerShell 5.1 v rouře mezi programy poškodí
  binární data. Soubor je spolehlivý.
- [ ] **F3.** Konfigurace Compose na VM:

  ```powershell
  ssh minishop-vm "mkdir -p ~/minishop"
  scp deploy/compose.yaml minishop-vm:minishop/
  ```

### G. Spuštění (na VM přes SSH)

- [ ] **G1.** Načtení image:

  ```bash
  docker load -i ~/minishop-images.tar && rm ~/minishop-images.tar
  docker image ls        # minishop-web:arm64, minishop-inventory-api:arm64
  ```

- [ ] **G2.** Soubor `.env` – heslo k RabbitMQ vygeneruje server, nikde jinde neexistuje:

  ```bash
  cd ~/minishop
  cat > .env <<EOF
  RABBITMQ_USER=minishop
  RABBITMQ_PASSWORD=$(openssl rand -hex 24)
  WEB_PORT=80
  IMAGE_TAG=arm64
  EOF
  chmod 600 .env
  cat .env
  ```

  `WEB_PORT=80` – Web bude na standardním HTTP portu. `IMAGE_TAG=arm64` – Compose použije přenesené image.
  `chmod 600` – soubor smí číst jen uživatel `ubuntu`.
- [ ] **G3.** Start:

  ```bash
  docker compose up -d --no-build
  docker compose ps        # po ~1 minutě všechny tři služby "healthy"
  ```

  `--no-build`: na serveru nic nestavět (zdrojáky tu nejsou). `rabbitmq:4-management` si Docker stáhne sám
  (verze pro ARM existuje). Když něco není *healthy*: `docker compose logs web` (nebo `inventory-api`, `rabbitmq`).

### H. Ověření

- [ ] **H1.** V prohlížeči na PC: **`http://<veřejná IP>`** → MiniShop. Nabídka produktů se objeví po pár sekundách
  (katalog se synchronizuje ze skladu po startu).
- [ ] **H2.** Vytvořte objednávku a počkejte ~4–8 s → stav **Confirmed**. Celá sága (Web → RabbitMQ → sklad → zpět)
  teď běží v cloudu. 🎉
- [ ] **H3.** Z mobilu přes mobilní data (jiná IP) `http://<veřejná IP>` → **nenačte se** (timeout).
  Tohle je správně – security list pouští jen vaši IP.
- [ ] **H4.** (Volitelně) RabbitMQ UI přes SSH tunel – port 15673 je na VM jen na `127.0.0.1`, zvenku nedostupný:

  ```powershell
  ssh -L 15673:127.0.0.1:15673 minishop-vm
  ```

  a v prohlížeči http://localhost:15673 (uživatel `minishop`, heslo z `cat ~/minishop/.env`).
  Tunel = SSH „protáhne“ port ze serveru k vám; platí, dokud je SSH spojení otevřené.

### Nasazení nové verze (ručně, do kroku 8)

Na PC zopakovat F1 a F2, na VM:

```bash
docker load -i ~/minishop-images.tar && rm ~/minishop-images.tar
cd ~/minishop && docker compose up -d --no-build
```

Compose vymění jen kontejnery, jejichž image se změnila. Data ve volumes zůstanou.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `docs/kroky-cloud/krok-04-sit-a-vm.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Web/wwwroot/js/order-form-tracker.js` | náhrada za `crypto.randomUUID()` mimo HTTPS (viz Známá omezení) |

`deploy/compose.yaml` funguje na VM beze změny – rozdíly (port 80, štítek `arm64`, heslo) jsou jen
v `.env` na serveru.

Mimo repozitář: na PC `~/.ssh/minishop_oci`, `minishop_oci.pub`, `~/.ssh/config`; na VM `~/minishop/compose.yaml`
a `~/minishop/.env`.

## Rozhodnutí v tomto kroku

- **Port 80 dočasně jen z vaší IP** (plán v `Cloud-roadmap.md` říkal „80 a 443 odkudkoli“). Do kroku 5 nemá
  aplikace přihlášení ani HTTPS a kdokoli by mohl zakládat objednávky a přehrávat cizí průběhy. V kroku 5 se
  80 a 443 otevřou všem, protože před aplikací bude Caddy se zámkem.
- **Průvodce VCN, ne ruční skládání** – vytvoří i privátní subnet a NAT/Service Gateway, které teď nepotřebujeme.
  V kroku 9 (OpenTofu) popíšeme jen to, co MiniShop opravdu používá.
- **Štítek `arm64` místo `local`** – na PC zůstane image pro x86 (`docker compose up` v kroku 2 dál funguje nativně).
- **`docker save` + `scp` přes soubor** – spolehlivé ve Windows PowerShell 5.1; v kroku 7 to nahradí registry.
- **Heslo RabbitMQ vzniká na serveru** (`openssl rand`) – neputuje přes PC ani chat. Šestnáctkové znaky,
  aby nerozbily adresu `amqp://…` (viz `.env.example`).
- **iptables před Dockerem** – `netfilter-persistent save` po instalaci Dockeru by trvale uložilo i jeho dočasná
  pravidla.
- **Ubuntu 24.04 (ne Oracle Linux)** – rozšířenější, víc návodů; Docker má oficiální podporu pro ARM.
- **2 OCPU / 12 GB = celý free limit** – MiniShop potřebuje zlomek, ale nevyužitý limit nic neušetří.

## Známá omezení

- **HTTP bez šifrování a bez přihlášení** – i z vaší IP jde provoz po internetu v čitelné podobě. Řeší krok 5.
- **Na HTTP nejsou „zabezpečené“ funkce prohlížeče.** Lokálně (`localhost`) je prohlížeč má, na `http://<IP>` ne.
  Narazilo na to sledování průběhu objednávky (krok 12A): `crypto.randomUUID()` chyběla, skript spadl a průběh se
  neukládal. `order-form-tracker.js` má proto náhradu přes `crypto.getRandomValues`. Po kroku 5 (HTTPS) zmizí i důvod.
- **Vaše domácí IP se může změnit** (typicky po restartu routeru) → SSH i web přestanou odpovídat (timeout).
  Oprava: A2 a upravit obě pravidla v B3.
- **Veřejná IP je „ephemeral“** – drží, dokud VM existuje (i přes stop/start), ale po smazání a novém vytvoření VM
  bude jiná. Pro doménu v kroku 5 ji převedeme na *reserved* IP.
- **Ruční nasazení** – kopie `compose.yaml` na VM se může rozejít s repozitářem; image nemá verzi (vždy `arm64`).
  Řeší kroky 7 a 8.
- **Jedna VM, žádné zálohy** – data (SQLite, fronty) jsou jen ve volumes na boot disku VM. Řeší krok 12.
- **`get.docker.com`** spouští skript z internetu pod rootem. Je oficiální od Dockeru a běžně používaný, ale
  „čistší“ cesta je ruční přidání apt repozitáře (stejný výsledek, víc kroků).
- **Docker a iptables:** Docker publikované porty (80 → kontejner) vede přes řetězec `FORWARD`, kam si vkládá
  vlastní pravidla, ne přes `INPUT`. Pravidla z E2 jsou proto hlavně pro služby mimo Docker a jako pojistka –
  viz *Vyzkoušejte sami*.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| *Virtual cloud networks* (compartment `minishop`) | `minishop-vcn`, 2 subnety |
| Default Security List, ingress | 22 a 80 ze `<vaše IP>/32`, ICMP |
| `oci compute instance list --compartment-id <minishop>` | `minishop-vm`, `RUNNING`, `VM.Standard.A1.Flex` |
| `ssh minishop-vm` | přihlášení bez hesla (jen passphrase klíče, pokud jste ji zadal) |
| `docker compose ps` na VM | `rabbitmq`, `inventory-api`, `web` – `healthy` |
| `http://<IP>` z PC | MiniShop, objednávka → Confirmed |
| `http://<IP>` z mobilních dat | timeout |
| *Cost Analysis* po dni | 0 |

## Vyzkoušejte sami

1. **Restart serveru.** `sudo reboot`, po minutě znovu `http://<IP>` → MiniShop běží a objednávky z minula
   jsou tam. Zařídil to `restart: unless-stopped` v `compose.yaml` a volumes.
2. **Kde je vrátný ①.** V security listu dočasně smažte pravidlo pro port 80 → web dá timeout (paket se k VM
   vůbec nedostane). Vraťte ho.
3. **Kde je vrátný ②.** Na VM `sudo iptables -L FORWARD -n --line-numbers` – nahoře `DOCKER-USER`/`DOCKER-FORWARD`,
   dole Oracle `REJECT`. Docker své pravidlo vložil před REJECT, proto by web šel i bez E2. Zkuste
   `sudo iptables -D INPUT -p tcp --dport 80 -m state --state NEW -j ACCEPT` → web stále jde. Pak
   `sudo iptables-restore < /etc/iptables/rules.v4 && sudo systemctl restart docker` vrátí uložený stav.
4. **Data přežijí nové image.** Nasaďte novou verzi (sekce *Nasazení nové verze*) – objednávky zůstanou.
   `docker compose down -v` by je smazal (to nedělejte, jen vězte).
5. **Kolik toho MiniShop bere.** `docker stats --no-stream` na VM → stovky MB z 12 GB. `htop` (`sudo apt install htop`)
   ukáže 2 ARM jádra.
