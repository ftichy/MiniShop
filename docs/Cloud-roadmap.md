# MiniShop: cesta do cloudu a CI/CD

18. 10. 2026 · František

MiniShop nasadíme do skutečného cloudu ve 12 malých krocích. Cíl není jen „aby to běželo“, ale projít si všechno,
co se při nasazení dělá: zabalit aplikaci do kontejnerů, založit cloudový účet a síť, zajistit HTTPS, a nakonec vše
zautomatizovat pipeline v GitHub Actions a infrastrukturou jako kódem. Po každém kroku aplikace funguje.
Dokument je zatím jen plán, do kódu se nic nemění.

**Rozpočet:** max. 100 Kč měsíčně. **Uživatel:** jen já. **Výsledná cena podle plánu: 0 Kč měsíčně.**

## Úvod: co znamená „nasadit do cloudu“

Dnes MiniShop spouští Aspire na vašem počítači: vytvoří kontejner RabbitMQ, spustí dva procesy (Web, Inventory.Api),
předá jim adresy a ukáže dashboard. V cloudu Aspire AppHost **neběží**. Všechno, co za vás dělá, musí převzít něco jiného:

| Co dnes dělá Aspire / váš počítač | Kdo to převezme v cloudu | Krok |
| --- | --- | --- |
| Spustí Web a Inventory.Api jako procesy | Docker image + Docker Compose | 1, 2 |
| Spustí RabbitMQ a vygeneruje heslo | Kontejner v Compose, heslo jako secret | 2, 10 |
| Předá adresy (`ConnectionStrings:messaging`, `inventory-api`) | Proměnné prostředí v Compose | 2 |
| `https://localhost` s vývojovým certifikátem | Doména + Let's Encrypt (Caddy) | 5 |
| Dashboard s trasami a logy | Samostatný Aspire Dashboard v cloudu | 11 |
| SQLite soubory na disku vývojáře | Trvalý disk (volume) + zálohy | 2, 12 |
| `dotnet run` ručně | CI/CD pipeline (GitHub Actions) | 6–8 |
| Počítač prostě „je“ | Cloudová VM, síť, firewall – nejdřív ručně, pak kódem | 3, 4, 9 |

Přirovnání ze života: dosud jste vařili doma, kde máte všechno po ruce. Teď si pronajímáte kuchyň v gastro centru.
Musíte si přivézt vybavení v krabicích (kontejnery), domluvit se s pronajímatelem na klíčích a pravidlech (účet, IAM,
firewall), pověsit ceduli s adresou (doména, HTTPS) a nakonec si objednat dodávkovou službu, která novou várku přiveze
sama (CI/CD).

### Základní pojmy

| Pojem | Význam | Příklad v MiniShopu |
| --- | --- | --- |
| Image (obraz) | Zabalená aplikace se vším, co potřebuje ke spuštění. Neměnná. | `ghcr.io/ftichy/minishop-web:1a2b3c4` |
| Kontejner | Běžící instance image | kontejner `web` na VM |
| Registry | „Sklad“ imagí, odkud si je server stáhne | GitHub Container Registry (GHCR) |
| Docker Compose | Popis více kontejnerů a jejich propojení v jednom YAML | `deploy/compose.yaml` |
| VM (virtuální stroj) | Pronajatý počítač v cloudu | OCI instance `minishop-vm` |
| VCN / subnet | Virtuální síť v cloudu, ve které VM žije | `minishop-vcn` |
| Security list / firewall | Pravidla, které porty jsou zvenku otevřené | 22 (SSH), 80, 443 |
| Reverse proxy | Vrátný před aplikací: HTTPS, certifikát, přihlášení | Caddy |
| CI (Continuous Integration) | Každá změna se automaticky sestaví a otestuje | `dotnet test` v GitHub Actions |
| CD (Continuous Delivery/Deployment) | Otestovaná změna se automaticky nasadí | `docker compose pull && up` přes SSH |
| IaC (Infrastructure as Code) | Infrastruktura popsaná kódem, ne klikáním | OpenTofu soubory v `infra/` |
| Secret | Citlivá hodnota, která nesmí do gitu | heslo RabbitMQ, SSH klíč |

Zlaté pravidlo: **co jste jednou udělali ručně, to v dalším kroku popíšete kódem.** Ruční krok je na pochopení,
kód je na opakování.

## Výběr cloudu: proč Oracle Cloud Always Free

MiniShop potřebuje **trvale běžící** procesy: RabbitMQ, consumery zpráv a `OutboxWorker`. To vylučuje
„serverless“ služby, které se při nečinnosti uspí (a tím i levné varianty Azure Container Apps).
Paměť: Web ~200 MB, Inventory.Api ~150 MB, RabbitMQ ~150 MB, Caddy a dashboard ~200 MB → potřebujeme alespoň **2 GB RAM**.

Ceny k 10/2026 (ověřené z ceníků a srovnávačů, s DPH přepočteno kurzem ~24,5 Kč/€):

| Poskytovatel | Co dostanu | Cena / měsíc | Verdikt |
| --- | --- | --- | --- |
| **Oracle Cloud (OCI) Always Free** | VM Ampere A1 (ARM) 2 OCPU / 12 GB RAM, 200 GB disk, 10 TB přenosu, Object Storage 20 GB, load balancer | **0 Kč** | **Doporučeno** |
| Hetzner Cloud | CX23 2 vCPU / 4 GB | ~180 Kč (po zdražení 2026; CX/CAX navíc nyní nelze objednat) | nad rozpočet |
| OVHcloud / Netcup VPS | 2–4 vCPU / 4 GB | ~120–150 Kč | plán B, ale jen „holá VPS“ |
| Azure (Container Apps + RabbitMQ + registry) | to, kam Aspire míří nativně (`azd up`) | odhadem 300–600 Kč (stálé kontejnery + ACR) | nad rozpočet; free B1s má jen 1 GB RAM |
| AWS / Google Cloud free tier | 1 GB RAM (e2-micro) nebo dočasné kredity | 0 Kč jen dočasně / málo paměti | nevhodné |

**Proč OCI:**

- **0 Kč a výkon s rezervou.** 12 GB RAM je víc, než MiniShop kdy využije.
- **Je to skutečný cloud, ne jen VPS.** Naučíte se pojmy, které jsou v AWS i Azure stejné: tenancy, compartment,
  IAM politiky, virtuální síť, security list, objektové úložiště, rozpočty a upozornění. U VPS byste dostali jen IP adresu.
- **Podpora IaC.** Oficiální provider pro Terraform/OpenTofu.
- **Region Frankfurt** je blízko (nízká latence z ČR).

**Na co si dát pozor (a jak to řešíme):**

| Riziko | Řešení v plánu |
| --- | --- |
| VM je **ARM** (ne x86) | Image budeme stavět pro `linux/arm64` i `linux/amd64` (krok 1, 7). Dobrá lekce navíc. |
| Oracle v 2026 snížil free limity (dříve 4 OCPU / 24 GB) | Pořád nám to stačí 10×. |
| **Nečinné free VM může Oracle zrušit** (7 dní CPU, síť i paměť pod 20 %) – MiniShop bude skoro pořád nečinný | Převést účet na **Pay As You Go** (free limity zůstávají, reclaim se podle zkušeností uživatelů netýká placených účtů – ověřit v aktuálních podmínkách) + **rozpočet s upozorněním** (krok 3). Navíc vše bude v kódu a zálohách, takže zrušení VM = 10 minut obnovy (krok 12). |
| „Out of host capacity“ při zakládání A1 VM | Zkusit jinou availability domain / později; s PAYG bývá kapacita lepší. |
| Domovský region (home region) **nejde změnit** a free zdroje jsou jen v něm | Zvolit při registraci uvážlivě (Frankfurt). |
| Registrace chce platební kartu (ověřovací blokace) | Bez karty to u žádného velkého cloudu nejde. |

Zbytek je také zdarma: **GitHub Actions** (2 000 minut/měsíc i pro soukromé repo), **GitHub Container Registry**,
**Let's Encrypt** certifikáty a doména třetího řádu z **DuckDNS** (např. `minishop-ft.duckdns.org`).

**Plán B:** když OCI odmítne registraci nebo nebude kapacita, kroky 1, 2 a 5–12 platí beze změny i pro libovolnou
VPS (OVH, Netcup, Hetzner). Mění se jen kroky 3, 4 a 9 (účet, síť, Terraform provider).

## Cílový stav

```
                  prohlížeč (jen já)
                        │ HTTPS 443, heslo (basic auth)
                        ▼
  ┌──────────── OCI VM minishop-vm (ARM, Ubuntu, Docker) ────────────────┐
  │  caddy ──► web:8080 (Ordering + UI) ──HTTP──► inventory-api:8080     │
  │              │  ▲                                 │  ▲               │
  │              ▼  │       rabbitmq:5672             ▼  │               │
  │              └──┴──────── (fronty) ───────────────┴──┘               │
  │  aspire-dashboard (OTLP: trasy, logy, metriky)                       │
  │  volumes: /data (ordering.db, journeys.db, inventory.db, klíče),     │
  │           rabbitmq-data                                              │
  └──────────────────────────────────────────────────────────────────────┘
        ▲ docker compose pull/up (SSH)                ▲ docker pull
        │                                             │
  GitHub Actions: build → test → image (arm64+amd64) → GHCR → deploy
        ▲
     git push
```

## Přehled kroků

Kroky stavějí na sobě a kdykoli jde přestat. Po fázi B aplikace běží v cloudu (ručně nasazená).
Fáze C ji zautomatizuje, fáze D z ní udělá něco, co se dá provozovat.

| Krok | Fáze | Co přinese | Nový nástroj | Cena |
| --- | --- | --- | --- | --- |
| 1. Kontejnerizace a produkční konfigurace | A – připravit lokálně | Image Webu a skladu, konfigurace z proměnných prostředí | Dockerfile, Docker | 0 |
| 2. Docker Compose („produkce na mém PC“) | A | Celá aplikace bez Aspire, trvalá data ve volume | Docker Compose | 0 |
| 3. Cloudový účet, rozpočet, IAM | B – cloud ručně | Bezpečný účet, upozornění na útratu, uživatel s minimem práv | OCI Console | 0 |
| 4. Síť a VM ručně, první nasazení | B | MiniShop běží v cloudu na IP adrese | SSH, VCN, security list | 0 |
| 5. Doména, HTTPS a zámek | B | `https://…duckdns.org` s certifikátem a heslem | Caddy, DuckDNS, Let's Encrypt | 0 |
| 6. CI: build a testy při každém push/PR | C – automatizace | Rozbitý kód se nedostane do `main` | GitHub Actions | 0 |
| 7. Image do registry | C | Verzované image pro ARM i x86 v GHCR | GHCR, Docker Buildx | 0 |
| 8. CD: automatické nasazení | C | `git push` → za pár minut nová verze v cloudu | Actions + SSH, Environments | 0 |
| 9. Infrastruktura jako kód | C | VM a síť vzniknou příkazem `tofu apply` | OpenTofu, cloud-init | 0 |
| 10. Secrets a konfigurace | D – provoz | Žádné heslo v gitu, rotace hesel | GitHub Secrets, `.env` | 0 |
| 11. Monitoring a logy v cloudu | D | Trasy a logy jako v Aspire, upozornění při výpadku | Aspire Dashboard, uptime monitor | 0 |
| 12. Zálohy a obnova po havárii | D | Data přežijí smazání VM, nacvičená obnova | Object Storage, `sqlite3 .backup` | 0 |

Volitelné pokračování na konci dokumentu (migrace databáze, Aspire publish, Kubernetes, Azure).

---

## Fáze A: připravit aplikaci lokálně

## Krok 1: Kontejnerizace a produkční konfigurace

**Teorie.** **Image** je „krabice“, ve které je aplikace i runtime .NET. Kdo má Docker, spustí ji stejně – na vašem PC,
na VM v cloudu i v CI. Tím mizí věta „u mě to funguje“. Návod na stavbu image je **Dockerfile** (dvoufázový:
SDK aplikaci zkompiluje, do malé runtime image se zkopíruje jen výsledek). Druhé téma je **12-factor konfigurace**: aplikace bere nastavení (connection
stringy, adresy) z **proměnných prostředí**, ne z kódu nebo souborů v image. Stejná image pak běží všude, liší se jen
proměnné.

**Proč.** V cloudu nebude Aspire, který dnes službám „podstrčí“ adresy. A image je jediná věc, kterou pipeline
přenese na server.

**V MiniShopu:**

- `Dockerfile` pro Web i sklad, `.dockerignore`; aplikace běží jako neprivilegovaný uživatel `app`,
  trvalá data ve složce `/data`; build připravený pro ARM i x86 (`-a $TARGETARCH` – VM v OCI je ARM).
- Konfigurace z proměnných prostředí (ASP.NET Core to umí sám, `__` = dvojtečka):
  `ConnectionStrings__messaging=amqp://…@rabbitmq:5672`,
  `Services__inventory-api__http__0=http://inventory-api:8080`,
  `ConnectionStrings__Ordering=Data Source=/data/ordering.db`.
- **Data Protection klíče** do `/data/keys` (`PersistKeysToFileSystem`). Bez toho se po každém restartu kontejneru
  vygenerují nové klíče a rozpracovaný formulář objednávky spadne na antiforgery tokenu.
- **Forwarded headers**: aplikace poběží za Caddy, takže musí věřit hlavičkám `X-Forwarded-Proto/For`.
  Jinak si myslí, že běží na HTTP. Stačí vestavěná proměnná `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (krok 2, 5).
- Serilog v kontejneru: logy na konzoli stačí (čte je `docker logs`), soubor jen do volume.
- `/health` je už namapované vždy (`MapDefaultEndpoints`) – využije ho Compose i pipeline.

**Hotovo, když:** `docker run` image Webu odpoví na `/health`. Testy procházejí, `dotnet run` přes Aspire funguje dál.

## Krok 2: Docker Compose – „produkce na mém PC“

**Teorie.** **Docker Compose** popisuje v jednom YAML souboru více kontejnerů, jejich síť, proměnné a **volumes**
(trvalé disky). Kontejner je dočasný – když ho smažete, smaže se i všechno, co do sebe zapsal. Volume přežije.
Kontejnery v jedné Compose síti se najdou podle jména služby (`rabbitmq`, `inventory-api`) – to je obdoba service
discovery z Aspire.

**Proč.** Je to přesně to, co poběží na VM. Když to funguje lokálně, v cloudu řešíme už jen cloud.
Aspire zůstává pro vývoj, Compose je pro „produkci“.

**V MiniShopu:** nová složka `deploy/` s `compose.yaml`:

- služby `rabbitmq` (`rabbitmq:4-management`, volume `rabbitmq-data`), `inventory-api`, `web`;
- `depends_on` s `condition: service_healthy` (obdoba `WaitFor` v AppHostu) a `healthcheck` na `/health`;
- volume `minishop-data` pro SQLite soubory a klíče;
- `.env` soubor s heslem RabbitMQ (v `.gitignore`, do repa jen `.env.example`);
- zatím bez Caddy, Web publikovaný na `localhost:8080`.

**Hotovo, když:** `docker compose up` spustí celý MiniShop, objednávka projde ságou (Confirmed) a po
`docker compose down` + `up` data zůstanou.

---

## Fáze B: cloud ručně

## Krok 3: Cloudový účet, rozpočet a IAM

**Teorie.** Než cokoli založíte, zabezpečte účet. **Root/administrátorský účet** se nepoužívá na každodenní práci.
**IAM** (Identity and Access Management) určuje, kdo smí co: uživatelé, skupiny a **politiky**
(„skupina `minishop-admins` smí spravovat VM v compartmentu `minishop`“). **Compartment** je složka na zdroje – jde
v ní vidět a smazat všechno, co k projektu patří. **Rozpočet (budget)** pošle e-mail, když útrata přesáhne limit.
Je to pojistka proti zapomenuté drahé službě.

**Proč.** Nejčastější „cloudová katastrofa“ začátečníka není hack, ale nečekaný účet. A druhá nejčastější je
uniklý klíč s administrátorskými právy.

**V MiniShopu (OCI Console):**

1. Registrace, home region **Germany Central (Frankfurt)**, zapnout **MFA**.
2. Převod na **Pay As You Go** (kvůli rušení nečinných VM a kapacitě; Always Free limity zůstávají).
3. **Budget** na compartment s upozorněním už při **1 Kč** skutečné útraty.
4. Compartment `minishop`, skupina + politika, uživatel pro Terraform/CLI s API klíčem.
5. Instalace OCI CLI lokálně, `oci iam region list` jako test přihlášení.

**Hotovo, když:** máte MFA, rozpočet s e-mailovým upozorněním a CLI pracuje pod omezeným uživatelem.

## Krok 4: Síť a VM ručně, první nasazení

**Teorie.** **VCN** (Virtual Cloud Network) je vaše soukromá síť v cloudu. V ní je **subnet** (veřejný = má cestu do
internetu přes **Internet Gateway**). Co smí dovnitř, určuje **security list** (cloudový firewall).
Na VM navíc běží vlastní firewall OS – v OCI obrazech Ubuntu jsou **iptables** pravidla, která porty 80/443 blokují
i po otevření v security listu (klasická past, na které si to vyzkoušíte). Přístup na VM je přes **SSH klíč**, ne heslo.

**Proč.** Kliknout si to jednou ručně je nejlepší způsob, jak pochopit, co v kroku 9 popíšeme kódem.

**V MiniShopu:**

1. VCN wizard „with Internet Connectivity“, security list: 22 jen z vaší IP, 80 a 443 odkudkoli.
2. VM `VM.Standard.A1.Flex` (1–2 OCPU, 6–12 GB), Ubuntu 24.04 ARM, váš veřejný SSH klíč.
3. Na VM: Docker Engine + Compose plugin, úprava iptables pro 80/443.
4. Image zatím ručně: `docker buildx build --platform linux/arm64` → `docker save | ssh … docker load`
   (registry přijde v kroku 7), zkopírovat `deploy/` a `docker compose up -d`.

**Hotovo, když:** `http://<veřejná-IP>` zobrazí MiniShop a objednávka projde až do Confirmed.
Uvidíte, že aplikace je teď **veřejně dostupná bez hesla** – to opraví krok 5.

## Krok 5: Doména, HTTPS a zámek

**Teorie.** **DNS** převádí jméno na IP adresu. **TLS certifikát** dokazuje, že server je opravdu ten, za koho se
vydává, a šifruje provoz. **Let's Encrypt** vydává certifikáty zdarma a automaticky (ověří, že doménu ovládáte,
přes HTTP výzvu na portu 80). **Caddy** je reverse proxy, která certifikát získá a obnovuje sama.
MiniShop nemá přihlašování – protože k aplikaci přistupujete jen vy, zamkneme ji na úrovni proxy (**basic auth**).

**Proč.** Bez HTTPS jde heslo po síti čitelně. Bez zámku si kdokoli může „objednávat“ (boti skenují veřejné IP
během minut – v logu Caddy to uvidíte).

**V MiniShopu:**

- DuckDNS subdoména → veřejná IP VM.
- Služba `caddy` v `compose.yaml` + `deploy/Caddyfile`: `reverse_proxy web:8080`, `basic_auth`, porty 80/443,
  volume na certifikáty.
- Web už není publikovaný ven (jen přes Caddy). RabbitMQ management UI jen přes SSH tunel.
- Volitelně: SSH (port 22) zavřít úplně a chodit přes **Tailscale** (soukromá síť zdarma).

**Hotovo, když:** `https://minishop-xx.duckdns.org` má platný certifikát, chce heslo a `http://` přesměruje na `https://`.

---

## Fáze C: automatizace

## Krok 6: CI – build a testy při každém push a pull requestu

**Teorie.** **Continuous Integration** = každá změna se automaticky sestaví a otestuje na čistém stroji.
**GitHub Actions** spouští **workflow** (YAML v `.github/workflows/`) na **runneru** (dočasná VM od GitHubu).
Workflow se skládá z **jobů** a ty z **kroků**. **Branch protection** zakáže sloučit PR, dokud CI neprojde.

**Proč.** Testy, které musíte pamatovat spustit, se jednou nespustí. CI je „druhý pár očí“, který nikdy nezapomene.

**V MiniShopu:** `.github/workflows/ci.yml` – na `push` a `pull_request`: `actions/setup-dotnet` (10.0),
cache NuGet, `dotnet build MiniShop.slnx`, `dotnet test MiniShop.slnx` (testy nepotřebují RabbitMQ ani Docker – běží
nad `InProcessIntegrationBus`), výsledky testů jako artefakt. Ochrana větve `main`.

**Hotovo, když:** PR s rozbitým testem má červený křížek a nejde sloučit.

## Krok 7: Image do registry

**Teorie.** **Container registry** je sklad imagí. Pipeline image vytvoří a nahraje (**push**), server si ho stáhne
(**pull**). **Tag** je štítek verze. `latest` je pohodlný, ale neříká, *co* běží – proto tagujeme i **SHA commitu**.
Image je **neměnná**: stejný tag = stejný obsah, a návrat k předchozí verzi je jen změna tagu.
**Multi-arch image** obsahuje varianty pro ARM i x86 pod jedním tagem.

**Proč.** Přenášet image přes `docker save | ssh` (krok 4) nejde automatizovat ani vrátit zpět.

**V MiniShopu:** job `publish-images` (jen na `main`, po úspěšném CI): přihlášení do **GHCR** přes `GITHUB_TOKEN`,
`docker/build-push-action` pro `linux/amd64,linux/arm64` (Dockerfile z kroku 1), tagy `sha-<commit>` a `latest`.
Image `ghcr.io/ftichy/minishop-web` a `…/minishop-inventory-api`. Na VM `docker login ghcr.io` s tokenem jen pro čtení.

**Hotovo, když:** v GitHubu jsou v Packages obě image a VM je stáhne přes `docker compose pull`.

## Krok 8: CD – automatické nasazení

**Teorie.** **Continuous Delivery** = každá změna je připravená k nasazení (jedním klikem).
**Continuous Deployment** = nasadí se sama. **Push model:** pipeline se přihlásí na server (SSH) a spustí nasazení.
(Opak je **pull model** – server si sám hlídá nové verze, viz GitOps.) **GitHub Environments** umožní
`production` chránit ručním schválením a mít pro něj vlastní secrets. **Smoke test** po nasazení ověří, že aplikace žije;
když ne, pipeline nasadí předchozí tag (**rollback**).

**Proč.** Nasazení „ručně podle návodu v hlavě“ je nejčastější zdroj chyb. Pipeline to dělá pokaždé stejně.

**V MiniShopu:** `.github/workflows/deploy.yml` – job `deploy` s environment `production`:
na VM uživatel `deploy` (jen Docker, ne root), soukromý SSH klíč v secrets, `scp` složky `deploy/`,
`IMAGE_TAG=sha-… docker compose pull && docker compose up -d`, pak `curl https://…/health` a rollback při chybě.

Pozor na EDA: při nasazení se kontejnery restartují. Díky outboxu, inboxu a persistentním frontám (kroky 4–8 EDA)
se **žádná zpráva neztratí** – jen se zpozdí. Krásný důkaz, proč to celé bylo potřeba.

**Hotovo, když:** změna textu na stránce + `git push` → za pár minut je změna v cloudu, aniž byste šli na server.

## Krok 9: Infrastruktura jako kód

**Teorie.** **IaC** popisuje infrastrukturu (síť, firewall, VM) v textových souborech. Nástroj (**OpenTofu** –
open-source větev Terraformu) porovná popis se skutečností a provede rozdíl: `tofu plan` ukáže, co by se změnilo,
`tofu apply` to provede. Aktuální stav si drží ve **state** souboru. **cloud-init** je skript, který VM provede
při prvním startu (instalace Dockeru, uživatel `deploy`, iptables).

**Proč.** Ruční konfigurace z kroku 4 se nedá zopakovat ani zkontrolovat. S IaC smažete VM a za 5 minut je tu
znovu – stejná. Změny infrastruktury procházejí code review jako kód.

**V MiniShopu:** složka `infra/` – provider `oracle/oci`, `network.tf` (VCN, subnet, gateway, security list),
`compute.tf` (A1 VM), `cloud-init.yaml`, `outputs.tf` (veřejná IP). State v OCI Object Storage (S3 kompatibilní backend).
Volitelně workflow `infra.yml`: `tofu plan` jako komentář v PR.

**Hotovo, když:** ručně vytvořenou VM smažete, `tofu apply` + pipeline z kroku 8 vše obnoví (zatím bez dat – ta řeší krok 12).

---

## Fáze D: provoz

## Krok 10: Secrets a konfigurace

**Teorie.** **Secret** je hodnota, jejíž únik škodí (hesla, klíče, tokeny). Pravidla: nikdy v gitu (ani v historii),
každý secret jen tam, kde je potřeba (**princip nejmenších oprávnění**), a musí jít **rotovat** (vyměnit) bez
přestavby aplikace. Konfigurace, která není tajná (adresy, úrovně logování), patří do proměnných prostředí v Compose.

**Proč.** V krocích 2–9 vzniklo několik secrets (heslo RabbitMQ, basic auth, SSH klíč, GHCR token, OCI API klíč).
Je čas je uklidit a sepsat.

**V MiniShopu:** GitHub Environment `production` secrets → pipeline vygeneruje `.env` na VM. Vlastní uživatel
RabbitMQ místo `guest`. Kontrola, že `.env`, `*.pem`, `*.tfstate` jsou v `.gitignore`. Zapnout GitHub
**secret scanning** a **Dependabot**. Nacvičit rotaci hesla RabbitMQ.

**Hotovo, když:** `git grep` nenajde žádné heslo a výměna hesla = změna secretu + nové spuštění pipeline.

## Krok 11: Monitoring a logy v cloudu

**Teorie.** Lokálně ukazuje trasy a logy Aspire dashboard. Ten existuje i **samostatně** jako kontejner
(`mcr.microsoft.com/dotnet/aspire-dashboard`), který přijímá OTLP – stačí službám nastavit
`OTEL_EXPORTER_OTLP_ENDPOINT`. Kód z kroku 11 EDA (ServiceDefaults) se nemění. Druhá věc je **externí monitoring**:
někdo zvenku, kdo každých 5 minut zkusí `/health` a pošle e-mail, když neodpoví.

**Proč.** Bez toho se o výpadku dozvíte, až to náhodou otevřete. A hledat chybu v ságe bez tras je utrpení
(to už víte z EDA).

**V MiniShopu:** služba `aspire-dashboard` v Compose (telemetrie jen v paměti), přístupná přes Caddy na
`/dashboard` nebo vlastní subdoméně, s přihlášením. Health check `/health` (obsahuje i `OutboxHealthCheck`) hlídá
zdarma UptimeRobot nebo healthchecks.io. Volitelně OCI Monitoring alarm na CPU/disk VM.

**Hotovo, když:** v cloudovém dashboardu najdete trasu objednávky podle `minishop.order.id` a zastavení kontejneru
`web` vám do pár minut pošle e-mail.

## Krok 12: Zálohy a obnova po havárii

**Teorie.** Záloha, ze které jste nikdy neobnovovali, není záloha. Dvě čísla: **RPO** (kolik dat smíte ztratit,
např. 1 den) a **RTO** (jak dlouho smí obnova trvat, např. 30 minut). SQLite soubor se nesmí kopírovat za běhu
obyčejným `cp` (může být rozepsaný) – správně `sqlite3 … ".backup …"`. Zálohy patří **mimo** VM (objektové úložiště),
jinak zmizí spolu s ní.

**Proč.** Free VM může Oracle zrušit, disk může selhat a schéma se dnes mění mazáním DB (`EnsureCreated`).

**V MiniShopu:** malý kontejner/cron na VM: každou noc `.backup` všech tří DB → OCI Object Storage
(20 GB zdarma), uchovat 7 dní (lifecycle pravidlo). Skript `restore.sh`. RabbitMQ se nezálohuje – zprávy jsou
dočasné a outbox je v SQLite.

**Hotovo, když:** proběhne **cvičná havárie**: `tofu destroy` → `tofu apply` → pipeline → `restore.sh`
a objednávky jsou zpět. Změříte si svoje RTO.

---

## Volitelně: kam dál

- **EF Core migrace místo `EnsureCreated`.** V produkci nejde při změně schématu smazat databázi.
  Migrace (`dotnet ef migrations add`) a jejich spuštění při nasazení jsou přirozený další krok.
- **Aspire publish.** Aspire umí z AppHostu vygenerovat `docker-compose.yaml` (`aspire publish` s integrací
  `Aspire.Hosting.Docker`). Zajímavé porovnat s ručním Compose z kroku 2 – až budete rozumět, co generuje.
- **Kubernetes (k3s).** Na 12 GB VM se vejde k3s. Naučíte se Deployment, Service, Ingress – ale pro jednoho
  uživatele je to kanón na vrabce.
- **Azure + `azd up`.** Nejpřímější cesta z Aspire do cloudu (Container Apps, Service Bus místo RabbitMQ).
  Stojí ale stovky Kč měsíčně – vhodné na vyzkoušení s kreditem nového účtu a následným smazáním resource group.

## Shrnutí nákladů

| Položka | Služba | Cena |
| --- | --- | --- |
| VM 2 OCPU / 12 GB, 50–200 GB disk | OCI Always Free | 0 Kč |
| Zálohy (≤ 20 GB) | OCI Object Storage | 0 Kč |
| Doména | DuckDNS | 0 Kč |
| Certifikát | Let's Encrypt | 0 Kč |
| CI/CD | GitHub Actions (≤ 2 000 min/měsíc) | 0 Kč |
| Registry | GHCR | 0 Kč |
| Monitoring | Aspire Dashboard, UptimeRobot | 0 Kč |
| **Celkem** | | **0 Kč** (rozpočet 100 Kč hlídá OCI Budget) |

Ceny ověřeny 18. 10. 2026; free limity se mění (Oracle je v 2026 snižoval) – před krokem 3 je zkontrolujte.

## Postup práce na každém kroku

Stejně jako u EDA: nejdřív shrnutí (teorie + co se změní v MiniShopu) a souhlas, pak malé změny, build + testy,
souhrn do `docs/kroky-cloud/krok-NN-nazev.md` (Jak to funguje, Nové soubory, Změněné soubory, Rozhodnutí,
Známá omezení, Jak ověřit, Vyzkoušejte sami) a aktualizace tabulky v `CLAUDE.md`.
Kroky v cloudové konzoli dělá uživatel sám (podle návodu) – Claude nemá přístup k účtu a nic v cloudu nezakládá.
