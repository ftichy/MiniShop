# Cloud krok 3: Cloudový účet, rozpočet a IAM (Oracle Cloud)

V tomto kroku se nepíše kód. Založíte účet v Oracle Cloud Infrastructure (OCI) a hned ho zabezpečíte:
dvoufázové přihlášení, upozornění na útratu, „složka“ na zdroje MiniShopu a technický uživatel s minimem práv,
pod kterým budou později pracovat nástroje (OCI CLI, OpenTofu v kroku 9). Po kroku 3 v cloudu ještě nic neběží
a nic nestojí – ale máte bezpečné místo, kam v kroku 4 postavíte VM.

Návod je psaný pro konzoli OCI k 10/2026. Oracle menu občas přesouvá – když položku nenajdete, napište její
název do **vyhledávání nahoře v konzoli** (např. „Budgets“, „Compartments“), to funguje spolehlivě.

## Jak to funguje

Přirovnání: pronajímáte si kancelář v obrovské administrativní budově (cloud). Než do ní cokoli nastěhujete:

| Kancelář | OCI | MiniShop |
| --- | --- | --- |
| Nájemní smlouva | **tenancy** (váš účet) | jedna tenancy |
| Hlavní klíč od všeho, leží v trezoru | **administrátor tenancy** (váš login) + **MFA** | používat jen na správu |
| Místnost pro jeden projekt | **compartment** | `minishop` |
| Vrátný se seznamem „kdo smí kam“ | **IAM politika** (policy) | skupina `minishop-admins` smí jen do `minishop` |
| Klíč od jedné místnosti pro řemeslníka | **technický uživatel** + **API klíč** | `minishop-automation` |
| Hlídač elektroměru, který volá při spotřebě | **budget** + upozornění | e-mail při první koruně útraty |
| Pojistka, která vypadne při přetížení | **kvóta** (quota) – volitelně | max. 2 OCPU / 12 GB v `minishop` |

**IAM (Identity and Access Management)** odpovídá na otázku „kdo smí co a kde“. V OCI se píše čitelnými větami:

```
Allow group minishop-admins to manage all-resources in compartment minishop
      └─ KDO ─────────────┘    └─ CO ──────────────┘  └─ KDE ────────────────┘
```

Všechno, co není výslovně povolené, je zakázané. To je **princip nejmenších oprávnění**: když uniká API klíč
technického uživatele (třeba omylem v gitu), útočník se dostane jen do compartmentu `minishop`, ne k celému účtu
a fakturaci.

**Proč technický uživatel, a ne váš login?** Váš login je administrátor celé tenancy. Kdyby jeho API klíč ležel
na disku a v GitHub Actions, je to jako nechat hlavní klíč pod rohožkou. Nástroje dostanou vlastní identitu,
kterou jde kdykoli zablokovat bez dopadu na vás.

**Proč rozpočet hned na začátku?** Nejčastější cloudová „katastrofa“ začátečníka není hack, ale účet za službu,
kterou omylem vytvořil a zapomněl. Rozpočet nic nezastaví, ale pošle e-mail dřív, než se z korun stanou tisíce.

**Free Trial vs. Always Free vs. Pay As You Go:**

- **Free Trial** – po registraci dostanete na 30 dní kredit (obvykle 300 USD). Během něj konzole dovolí
  vytvořit i placené věci (platí se z kreditu). Po 30 dnech se placené zdroje smažou.
- **Always Free** – zdroje označené v konzoli štítkem *Always Free-eligible* (u nás VM Ampere A1 do
  2 OCPU / 12 GB, 200 GB disku) jsou zdarma natrvalo.
- **Pay As You Go (PAYG)** – účet s platební kartou. Always Free zdroje zůstávají zdarma, platí se jen to, co je
  nad nimi. Podle zkušeností uživatelů Oracle u PAYG účtů neruší nečinné free VM (u čistě free účtu ano – po 7 dnech
  pod 20 % CPU) a bývá lepší dostupnost A1 kapacity. Nevýhoda: placenou službu jde vytvořit omylem → proto budget
  (a volitelně kvóta).

## Návod krok za krokem

Odškrtávejte si. Hodnoty označené 📝 si poznamenejte do bezpečné poznámky **mimo repozitář**
(budou potřeba v krocích 4 a 9).

### A. Registrace

- [ ] **A1.** Na https://www.oracle.com/cloud/free/ → *Start for free*. Vyplňte e-mail, jméno, zemi (Czech Republic).
- [ ] **A2.** **Cloud Account Name** = název tenancy (malá písmena, např. `ftichy`). Používá se při přihlášení. 📝
- [ ] **A3.** **Home Region: Germany Central (Frankfurt)** – `eu-frankfurt-1`.
  ⚠️ **Nejde později změnit** a Always Free zdroje fungují jen v domovském regionu. Frankfurt: nejblíž ČR
  a má 3 availability domains (když jedna nemá volnou A1 kapacitu, zkusíte jinou).
- [ ] **A4.** Ověření platební kartou (proběhne malá ověřovací transakce, která se vrátí). Bez karty to u žádného
  velkého cloudu nejde.
- [ ] **A5.** Počkejte na e-mail „Your Oracle Cloud account is fully provisioned“ (minuty až desítky minut).

### B. Zabezpečení administrátorského účtu

- [ ] **B1.** Přihlaste se (https://cloud.oracle.com → Cloud Account Name → e-mail + heslo).
- [ ] **B2.** **MFA (dvoufázové ověření).** Nové účty k němu konzole obvykle vyzve sama při prvním přihlášení.
  Pokud ne: ikona profilu (vpravo nahoře) → *My profile* → *2-Step verification* / *Security* → zapnout.
  Aplikace: Oracle Mobile Authenticator, Microsoft/Google Authenticator nebo jiná TOTP.
- [ ] **B3.** Uložte si **záložní kódy** (bypass codes) na bezpečné místo. Ztráta telefonu bez nich = ztráta účtu.
- [ ] **B4.** Zkontrolujte, že je vpravo nahoře vybraný region **Germany Central (Frankfurt)**.

### C. Rozpočet a upozornění na útratu

- [ ] **C1.** ☰ → *Billing & Cost Management* → *Budgets* → **Create Budget**.
  - Name: `minishop-budget`
  - Target: **Compartment** → kořenový compartment (= celá tenancy, název jako váš Cloud Account Name).
    Celá tenancy, ne jen `minishop` – chceme zachytit i věc vytvořenou omylem jinde.
  - Schedule: *Monthly*, Budget amount: **4** (v měně účtu; pro EUR ≈ 100 Kč).
- [ ] **C2.** Alert rule 1: Type **Actual Spend**, Threshold **Percentage of budget: 1 %**, e-mail váš.
  → přijde e-mail, jakmile skutečná útrata přesáhne ~1 Kč. Při plánu „0 Kč“ to znamená: **něco je špatně**.
- [ ] **C3.** Alert rule 2: Type **Forecast Spend**, Threshold **100 %** → varování dopředu podle trendu.
- [ ] **C4.** Bonus: ☰ → *Billing & Cost Management* → *Cost Analysis* – tady uvidíte, co kolik stojí (teď 0).

### D. Compartment pro MiniShop

- [ ] **D1.** ☰ → *Identity & Security* → *Compartments* → **Create Compartment**.
  - Name: `minishop`, Description: „MiniShop – výukové nasazení“, Parent: kořenový compartment.
- [ ] **D2.** Zkopírujte **OCID** compartmentu (`ocid1.compartment.oc1..aaaa…`). 📝
  OCID = jednoznačné ID každé věci v OCI, nástroje s ním pracují místo jmen.
- [ ] **D3.** Zkopírujte **OCID tenancy** (začíná `ocid1.tenancy.oc1..`). 📝 Kde ho najít:
  - do vyhledávání nahoře v konzoli napište **Tenancy details** → karta *Tenancy information* → OCID → **Copy**;
  - nebo ☰ → *Governance & Administration* → *Tenancy Management* → *Tenancy details*;
  - nebo ikona profilu vpravo nahoře → *Tenancy: <název>*;
  - OCID tenancy je zároveň OCID **kořenového compartmentu** (seznam v *Compartments*);
  - když nic z toho, přeskočte – objeví se v bodě F3 v náhledu konfigurace na řádku `tenancy=…`.

### E. Technický uživatel, skupina a politika

- [ ] **E1.** ☰ → *Identity & Security* → *Domains* → **Default** → *User management*.
- [ ] **E2.** **Groups → Create group**: `minishop-admins`, popis „Správa zdrojů v compartmentu minishop“.
- [ ] **E3.** **Users → Create user**: jméno `minishop-automation`, e-mail (OCI ho vyžaduje – můžete použít
  svůj s plusem, např. `vas.email+oci@…`), přidat do skupiny `minishop-admins`.
  ⚠️ **Odškrtněte** *Use the email address as the username* a do *Username* napište `minishop-automation`.
  Jinak se uživatelským jménem stane e-mail – a když je stejný jako u vašeho účtu, OCI hlásí
  „User with the same userName already exists.“
  Seznam uživatelů: *Domains* (nahoře vybraný **kořenový** compartment) → **Default** → *User management* → *Users*.
  Uživatel se do konzole přihlašovat nebude, stačí mu API klíč (bod F).
- [ ] **E4.** ☰ → *Identity & Security* → *Policies* → vlevo vybrat **kořenový** compartment → **Create Policy**.
  - Name: `minishop-admins-policy`, zapnout *Show manual editor* a vložit:

    ```
    Allow group 'Default'/'minishop-admins' to manage all-resources in compartment minishop
    ```

  - `'Default'/` = identity doména, ve které skupina je. Politika leží v kořeni, protože se odkazuje
    na compartment `minishop` jménem.
- [ ] **E5.** Zkopírujte **OCID uživatele** `minishop-automation` (detail uživatele). 📝

### F. API klíč pro nástroje

API klíč = dvojice klíčů. **Veřejný** nahrajete do OCI, **soukromý** zůstane jen u vás a podepisuje požadavky.
Heslo se po síti neposílá vůbec.

- [ ] **F1.** Detail uživatele `minishop-automation` → *API keys* → **Add API key** → *Generate API key pair*
  → **Download private key** → *Add*.
- [ ] **F2.** Složka `.oci` zatím neexistuje – vytvořte ji v PowerShellu:
  `New-Item -ItemType Directory "$env:USERPROFILE\.oci"`
  (Průzkumník složky s tečkou na začátku vytvořit neumí). Pak soukromý klíč ze Stažených souborů
  přesuňte a přejmenujte na `C:\Users\<vy>\.oci\minishop-automation.pem`:
  `Move-Item "$env:USERPROFILE\Downloads\<stažený>.pem" "$env:USERPROFILE\.oci\minishop-automation.pem"`
  ⚠️ **Nikdy ne do repozitáře** (`*.pem` je v `.gitignore`), ne do OneDrive/cloudu, neposílat e-mailem.
- [ ] **F3.** Po kliknutí na *Add* v bodě F1 ukáže konzole okno **Configuration file preview**, tedy hotový text
  konfigurace pro nástroje. Když jste okno zavřeli, najdete ho znovu: detail uživatele → *API keys* → u klíče
  menu **⋮** → *View configuration file*. Text zkopírujte do souboru `C:\Users\<vy>\.oci\config`
  (bez přípony, ne `config.txt`) a upravte řádek `key_file`:

  ```ini
  [DEFAULT]
  user=ocid1.user.oc1..aaaa…
  fingerprint=12:34:…
  tenancy=ocid1.tenancy.oc1..aaaa…
  region=eu-frankfurt-1
  key_file=~/.oci/minishop-automation.pem
  ```

  `~` místo `C:\Users\…` – stejný soubor pak funguje i v Docker kontejneru s OCI CLI (bod G) a v OpenTofu.

### G. OCI CLI a ověření práv

OCI CLI je napsané v Pythonu, který na PC nemáte. Nemusíte nic instalovat – Oracle vydává **oficiální image
s CLI** a Docker už máte. V PowerShellu:

```powershell
docker pull ghcr.io/oracle/oci-cli:latest

# funkce "oci" pro tento terminál (trvale: vložit do $PROFILE)
function oci { docker run --rm -it -v "$env:USERPROFILE\.oci:/oracle/.oci" `
  -e OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING=True ghcr.io/oracle/oci-cli:latest @args }
```

(Proměnná potlačí varování o právech souboru – Windows je přes Docker ukazuje jako „otevřené všem“.)

- [ ] **G1.** `oci os ns get` → vypíše `{"data": "…"}` (Object Storage namespace tenancy). 📝
  = přihlášení API klíčem funguje.
- [ ] **G2.** `oci compute instance list --compartment-id <OCID minishop>` → prázdný výsledek, žádná chyba.
  = skupina smí do compartmentu `minishop`.
- [ ] **G3.** **Negativní test:** `oci compute instance list --compartment-id <OCID tenancy>` →
  chyba `NotAuthorizedOrNotFound` (404). **Tohle je správně!** Technický uživatel do kořene nesmí.

Alternativa bez Dockeru: **Cloud Shell** (ikona `>_` nahoře v konzoli) – terminál v prohlížeči s předinstalovaným
CLI, přihlášený jako vy (administrátor). Hodí se na rychlé dotazy, ale neověří práva technického uživatele.

### H. (Volitelně) Kvóta jako pojistka – odloženo až po kroku 4

Budget jen upozorní. **Kvóta** zakáže vytvořit víc, než povolíte – placenou VM v `minishop` pak nejde
vytvořit vůbec.

- [ ] **H1.** Stav před kvótou (technický uživatel smí číst dostupnost ve svém compartmentu):

  ```powershell
  oci limits resource-availability get --compartment-id <OCID minishop> --service-name compute `
    --limit-name standard-a1-core-count --availability-domain <AD, např. xmpj:EU-FRANKFURT-1-AD-3>
  ```

  → `"effective-quota-value": null` (žádná kvóta) a `"available"` ve stovkách – tolik ARM jader by šlo vytvořit.
  Seznam AD: `oci iam availability-domain list --compartment-id <OCID minishop>`.
- [ ] **H2.** ☰ → *Governance & Administration* → *Tenancy Management* → **Quota policies** (nebo vyhledávání
  „Quota policies“) → vlevo **kořenový** compartment → **Create quota policy**:
  - Name: `minishop-quota`, Description: „Compartment minishop jen v mezích Always Free“
  - Statements (každý řádek zvlášť, tlačítko *+ Another statement*, nebo jeden blok podle konzole):

    ```
    zero compute-core quotas in compartment minishop
    zero compute-memory quotas in compartment minishop
    set compute-core quota standard-a1-core-count to 2 in compartment minishop
    set compute-memory quota standard-a1-memory-count to 12 in compartment minishop
    ```

  Čte se to: „v `minishop` vynuluj všechna jádra a paměť všech tvarů, ARM A1 pak povol do 2 jader a 12 GB“.
  Pořadí je důležité – pozdější `set` přepíše dřívější `zero` jen pro jmenovanou kvótu.
  ⚠️ Každý řádek = jeden příkaz. Za posledním řádkem **nesmí** zůstat prázdný řádek, jinak konzole hlásí
  „Syntax error starting with '' at statement 5 char 0“.
- [ ] **H3.** Ověření – zopakujte H1: `"effective-quota-value": 2.0` a `"available": 0` (VM z kroku 4 limit
  vyčerpala). Pro jiný tvar, např. `--limit-name standard-e4-core-count` → kvóta `0.0`.
- **Kdy:** až po kroku 4, kdy VM běží – hned ověříte, že kvóta free VM neblokuje.
  Smysl má hlavně s Pay As You Go (bod I) a během trialu (placené zdroje by čerpaly kredit).
- Kvóta hlídá jen **compute** v `minishop`. Placené služby jiného druhu (databáze, load balancer…) nebo zdroje
  mimo `minishop` dál hlídá jen budget.

### I. Pay As You Go (doporučeno, můžete odložit)

- [ ] **I1.** ☰ → *Billing & Cost Management* → *Upgrade and Manage Payment* → nejdřív
  **Change Payment Method** a zadejte kartu (bez ní je tlačítko níže šedé – stránka hlásí „Add a payment method
  to upgrade a subscription plan“). ⚠️ Ověření karty **dočasně zablokuje cca 100 USD** (u nás 2 296,80 Kč, potvrzení
  v aplikaci banky) – nejde o platbu, blokace se sama uvolní, obvykle během několika dní. Pak dlaždice *Pay As You Go* → **Upgrade your account**.
  Hotovo, když *Plan type* ukazuje Pay As You Go místo *Free Tier* (upgrade může trvat i několik hodin).
  Always Free zdroje zůstanou zdarma. Důvod: free VM s nečinným MiniShopem by jinak Oracle po 7 dnech mohl
  zastavit. Bez budgetu (C) **nedělejte**.
- Kdy nejpozději: před koncem 30denního trialu, nebo až uvidíte, že VM z kroku 4 běží a chcete ji nechat běžet.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `docs/kroky-cloud/krok-03-ucet-oci.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `.gitignore` | `*.pem`, `.oci/` – soukromé klíče nikdy do gitu |

Mimo repozitář (na vašem PC): `C:\Users\<vy>\.oci\config`, `C:\Users\<vy>\.oci\minishop-automation.pem`.

## Rozhodnutí v tomto kroku

- **Frankfurt jako domovský region** – nejbližší k ČR, 3 availability domains (víc šancí na volnou A1 kapacitu).
- **Budget na celou tenancy s prahem 1 %** – plán stojí 0 Kč, takže každá útrata je signál chyby.
- **Technický uživatel s právy jen na compartment** – nástroje (CLI, OpenTofu, později GitHub Actions)
  nikdy nepracují pod administrátorem.
- **OCI CLI z Docker image** – bez instalace Pythonu; stejný `~/.oci/config` použije OpenTofu v kroku 9.
- **`manage all-resources` v compartmentu** – na výukový projekt rozumný kompromis. V praxi by se práva
  dělila jemněji (síť, VM, úložiště zvlášť).
- **Kvóta volitelná** – názvy kvót pro A1 se mezi zdroji liší a chybná kvóta by v kroku 4 zablokovala i free VM.
- **Pay As You Go hned, ne až po potížích** – snižuje riziko chyby *Out of host capacity* v kroku 4 a rušení
  nečinné VM. Cena: dočasná blokace ~100 USD na kartě při jejím přidání.

## Známá omezení

- **Free limity se mění.** Oracle v 2026 snížil A1 na 2 OCPU / 12 GB (dříve 4 / 24). Stále ~50× víc, než MiniShop
  potřebuje (~250 MB).
- **Rušení nečinných VM a chování PAYG** popisují hlavně zkušenosti uživatelů, ne oficiální dokumentace –
  proto je všechno od kroku 9 v kódu a od kroku 12 v zálohách: zrušená VM = obnova za pár minut.
- **Budget reaguje se zpožděním** (náklady se vyhodnocují v řádu hodin). Je to hlídač, ne pojistka.
- **Klíč technického uživatele je na disku nešifrovaný.** Na osobním PC přijatelné; v kroku 8/10 se do GitHubu
  dostane jako secret, ne jako soubor.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| Přihlášení do konzole | chce MFA kód |
| *Budgets* | `minishop-budget`, 2 alert rules, cíl kořenový compartment |
| *Compartments* | `minishop` (Active) |
| *Policies* (kořen) | `minishop-admins-policy` s jedním příkazem |
| `oci os ns get` | `{"data": "…"}` |
| `oci compute instance list --compartment-id <minishop>` | bez chyby, prázdné |
| `oci compute instance list --compartment-id <tenancy>` | `NotAuthorizedOrNotFound` |
| `git status` v repozitáři | žádný `.pem` ani `config` |

## Vyzkoušejte sami

1. **Nejmenší oprávnění na vlastní oči.** Zkuste `oci iam user list --compartment-id <OCID tenancy>` pod
   technickým uživatelem → zamítnuto. V Cloud Shellu (jako administrátor) totéž projde.
2. **Odvolání přístupu.** Odeberte `minishop-automation` ze skupiny `minishop-admins` a zopakujte G2 → zamítnuto.
   Vraťte zpět. Takhle rychle se v praxi „zavírá“ uniklý klíč (nebo klíč smažete a vygenerujete nový).
3. **Politika se čte jako věta.** Změňte v politice `manage` na `inspect` a zkuste v kroku 4 vytvořit VM
   pod technickým uživatelem – nepůjde, jen číst. (`inspect` < `read` < `use` < `manage`.)
4. **Kde je co.** V konzoli přepněte region na jiný (např. Amsterdam) a podívejte se na *Compartments* –
   compartment `minishop` je vidět (IAM je globální), ale VM z kroku 4 uvidíte jen ve Frankfurtu (zdroje jsou regionální).
