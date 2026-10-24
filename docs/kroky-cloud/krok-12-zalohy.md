# Cloud krok 12: Zálohy a obnova po havárii

Po kroku 11 o výpadku víte do pár minut. Ale co když VM zmizí úplně – Oracle zruší free instanci, selže disk,
nebo omylem smažete volume? Infrastrukturu postaví OpenTofu (krok 9) a aplikaci pipeline (krok 8), jenže
**data** – objednávky, sklad, průběhy – leží jen na disku VM. Teď je budete každou noc zálohovat **mimo VM**
a hlavně si **nacvičíte obnovu**: smažete VM a změříte, za jak dlouho jsou objednávky zpět.

## Jak to funguje

Přirovnání: kopie důležitých dokumentů patří do trezoru **v jiné budově** (bucket, ne disk VM). Kopii dělá
každou noc **automat** (systemd timer), **hlídač** (healthchecks.io) se ozve, když automat jednu noc vynechá,
a jednou za čas se **cvičně vyhoří** – jen tak víte, že se z kopií opravdu dá žít.

```
 VM ─ každou noc 02:30 UTC: minishop-backup.timer → backup.sh (uživatel deploy)
        │  ping /start ─────────────────────────────────────────────────────────► healthchecks.io
        │  docker run oci-cli: backup.py
        │     ├─ online backup SQLite: ordering.db, journeys.db (web-data), inventory.db (inventory-data)
        │     ├─ integrity_check každé kopie + Data Protection klíče (/data/keys)
        │     └─ minishop-RRRRMMDDTHHMMSSZ.tar.gz ──instance principal (bez hesla)──► bucket minishop-backups
        │  ping (OK) / ping /fail ──────────────────────────────────────────────► healthchecks.io ─(chybí/fail)─► e-mail
        │                                                                          bucket: lifecycle smaže > 7 dní
 obnova: restore.sh latest → stop web, inventory-api → stáhnout, zkontrolovat, vyměnit soubory → start
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **RPO** (Recovery Point Objective) | kolik dat smíte ztratit | až 24 h (noční záloha); před rizikovou akcí ruční záloha |
| **RTO** (Recovery Time Objective) | jak dlouho smí obnova trvat | změříte při cvičné havárii |
| **Online backup** | konzistentní snímek SQLite za běhu (`.backup`) | `sqlite3.Connection.backup()` v `backup.py` |
| **Instance principal** | VM se k OCI přihlásí sama za sebe, bez klíče | dynamická skupina `minishop-vm` + politika `minishop-backups` |
| **Lifecycle pravidlo** | Object Storage samo maže staré objekty | zálohy starší než 7 dní |
| **Dead man's switch** | hlídač, který se ozve, když se *nic* nestane | healthchecks.io čeká na ping každý den |
| **systemd timer** | plánovač úloh v Linuxu (moderní cron) | `minishop-backup.timer` → `minishop-backup.service` |

**Proč ne `cp`:** SQLite zapisuje do souboru po stránkách; kopie pořízená uprostřed transakce může být poškozená
(a poškozenou zálohu poznáte až při obnově – tedy pozdě). Online backup API vytvoří snímek jednoho okamžiku,
i když aplikace zrovna zapisuje. Každá kopie navíc projde `PRAGMA integrity_check` – před nahráním i před obnovou.

**Proč VM nesmí mazat:** politika dává VM jen `OBJECT_CREATE` (nové zálohy) a čtení. Kdyby VM někdo ovládl,
může smazat data na VM, ale ne zálohy. Staré zálohy maže služba Object Storage podle lifecycle pravidla.

**Co se nezálohuje:** RabbitMQ (zprávy jsou dočasné, pravda je v outboxech uvnitř SQLite), certifikáty Caddy
(vyžádá si nové), logy a telemetrie.

**Proč havárie smazáním VM, ne `tofu destroy`:** reserved IP i bucket se zálohami mají `prevent_destroy` –
`tofu destroy` skončí chybou (záměr). Smazání VM v konzoli navíc odpovídá nejpravděpodobnější skutečné havárii.

## Návod krok za krokem

### A. Identita VM – dynamická skupina (vy, konzole OCI)

- [ ] **A1.** ☰ → *Identity & Security* → *Domains* → **Default** (kořenový compartment) → *Dynamic groups* →
  **Create dynamic group**: Name **`minishop-vm`**, Description „VM MiniShopu (zálohy)“,
  *Match any rules defined below*, pravidlo:

  ```
  ALL {instance.compartment.id = '<OCID compartmentu minishop>'}
  ```

  Každá VM v compartmentu `minishop` (i ta budoucí z OpenTofu) do skupiny patří automaticky.

### B. Hlídač záloh – healthchecks.io (vy)

- [ ] **B1.** https://healthchecks.io → *Sign up* (zdarma 20 kontrol) → *Add Check*: Name `minishop-backup`,
  *Schedule* → **Cron** `30 2 * * *`, Time zone **UTC**, *Grace time* **1 hour** → *Save*.
- [ ] **B2.** Zkopírovat *Ping URL* (`https://hc-ping.com/…`) → GitHub → `production` → nový secret
  **`HEALTHCHECKS_BACKUP_URL`**. (Kdo URL zná, může hlídači lhát, že záloha proběhla – proto secret.)

### C. Bucket a politika (vy, OpenTofu)

- [ ] **C1.** V `infra/`: `tofu plan` → **`Plan: 3 to add, 0 to change, 0 to destroy`** (bucket, lifecycle, politika)
  + změna výstupu. `tofu apply`.

### D. Nové skripty na současnou VM (vy / Claude) – PŘED sloučením

Starý `deploy.sh` by nový secret odmítl (*Neznámý klíč*), proto se nový instaluje dřív, než se sloučí PR.

- [ ] **D1.** Nový `deploy.sh` a jednotky systemd z větve:

  ```powershell
  ssh minishop-vm 'B=https://raw.githubusercontent.com/ftichy/MiniShop/backup/krok12/deploy; curl -fsSL $B/deploy.sh -o /tmp/md && sudo install -m 755 /tmp/md /usr/local/bin/minishop-deploy && for f in minishop-backup.service minishop-backup.timer; do curl -fsSL $B/$f | sudo tee /etc/systemd/system/$f > /dev/null; done && sudo systemctl daemon-reload && sudo systemctl enable --now minishop-backup.timer && systemctl list-timers minishop-backup.timer --no-pager'
  ```

  (Jednoduché uvozovky – PowerShell pak `$B` a `$f` nechá být a pošle je na VM.)

### E. PR a nasazení (vy)

- [ ] **E1.** PR `backup/krok12` → ✅ → **Merge** → CI → *Nasazení* ✅. `deploy.sh` přinese do `/opt/minishop`
  `backup.py`, `backup.sh`, `restore.sh` a do `.env` `HEALTHCHECKS_BACKUP_URL`.

### F. První záloha (vy)

- [ ] **F1.** `ssh minishop-vm "sudo systemctl start minishop-backup; journalctl -u minishop-backup -n 15 --no-pager"`
  → `ordering.db: … kB`, …, `Záloha nahrána: minishop-backups/minishop-….tar.gz`.
- [ ] **F2.** OCI → *Buckets* → `minishop-backups` → objekt `minishop-….tar.gz`; healthchecks.io → kontrola **Up** (zelená).

### G. Cvičná havárie (vy) – změřte RTO

- [ ] **G1.** Vytvořte 2–3 objednávky, poznamenejte si jejich čísla. Ruční záloha (F1). **Poznamenejte čas.** ⏱
- [ ] **G2.** Havárie: Tailscale → *Machines* → `minishop-vm` → *Remove*; OCI → *Instances* → `minishop-vm` →
  *Terminate* (✅ smazat boot volume). Web je mrtvý, UptimeRobot pošle e-mail.
- [ ] **G3.** Nový Tailscale auth key (krok 9, F1) do `terraform.tfvars`; `tofu apply` →
  `Plan: 1 to add, 4 to change, 0 to destroy` (VM; reserved IP a 3 alarmy na novou VM).
- [ ] **G4.** Po ~5 min: Tailscale → `minishop-vm` → *Edit machine IPv4* → `100.126.167.103`;
  `ssh-keygen -R 100.126.167.103`; `DEPLOY_KNOWN_HOSTS` = `ssh minishop-vm "ssh-keyscan -t ed25519 100.126.167.103 2>/dev/null"`.
- [ ] **G5.** *Actions* → **Deploy** → *Run workflow* → SHA z `main` → MiniShop běží s **prázdnými** daty.
- [ ] **G6.** Obnova: `ssh minishop-vm "sudo -u deploy /opt/minishop/restore.sh latest"` → objednávky z G1 jsou zpět.
  **Čas** ⏱ − čas z G1 = vaše **RTO**.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `deploy/backup.py` | záloha (online backup SQLite + klíče → tar.gz → bucket), obnova (`latest`/jméno, `integrity_check`, původní soubory jako `*.before-restore-…`), `list` |
| `deploy/backup.sh` | noční záloha: `backup.py` v kontejneru `oci-cli`, pingy na healthchecks.io (`/start`, OK, `/fail`) |
| `deploy/restore.sh` | obnova: stop `web` + `inventory-api` → `backup.py restore` → start (i při chybě) |
| `deploy/minishop-backup.service`, `.timer` | systemd: záloha jako `deploy` každou noc 02:30 UTC (`Persistent`) |
| `infra/backup.tf` | bucket `minishop-backups` (`prevent_destroy`), lifecycle 7 dní, politika (VM: číst + vytvářet, ne mazat; služba Object Storage: lifecycle) |
| `docs/kroky-cloud/krok-12-zalohy.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/deploy.sh` | volitelné soubory (`backup.py`, `backup.sh`, `restore.sh` – starší commity je nemají), `cp -p`, secret `HEALTHCHECKS_BACKUP_URL` |
| `.github/workflows/deploy.yml` | posílá `HEALTHCHECKS_BACKUP_URL` (volitelný) |
| `infra/cloud-init.yaml.tftpl`, `infra/bootstrap.sh.tftpl`, `infra/compute.tf` | nová VM dostane jednotky systemd a zapnutý timer |
| `infra/variables.tf`, `infra/outputs.tf` | `identity_domain`, `vm_dynamic_group`, `backup_retention_days`; krok „restore“ v dalších krocích |
| `docs/secrets.md` | `HEALTHCHECKS_BACKUP_URL`, identita VM |

Mimo repozitář: dynamická skupina `minishop-vm`, účet healthchecks.io, secret `HEALTHCHECKS_BACKUP_URL`,
na současné VM jednotky systemd v `/etc/systemd/system/` a nový `minishop-deploy`.

## Rozhodnutí v tomto kroku

- **Instance principal místo klíče** – na VM není žádné heslo ani klíč k bucketu; identitu VM ověří OCI.
- **VM smí jen vytvářet a číst** – napadená VM zálohy nesmaže; mazání jen lifecycle pravidlem.
- **Skripty záloh přes `deploy.sh` z gitu** (ne cloud-init) – jejich změny jdou běžnou cestou CD. Jednotky
  systemd se mění zřídka, ty instaluje cloud-init (a ručně jednou na současnou VM).
- **Python v image `oci-cli`** – jeden kontejner má OCI CLI i `sqlite3` (online backup); na VM nic neinstalujeme.
  Image připnutý na datum (`20261007`).
- **Jeden archiv na zálohu** – všechny DB z jednoho okamžiku pohromadě; obnova = jeden soubor.
- **Obnova nemaže** – původní soubory zůstanou jako `*.before-restore-…` (ruční návrat zpět).
- **Healthchecks.io jako dead man's switch** – UptimeRobot (krok 11) se ptá zvenku, ale záloha nemá co odpovídat;
  hlídač proto čeká, že se ozve on.
- **Bez zálohy RabbitMQ a certifikátů** – dočasná data / vyžádají se znovu.

## Výsledek cvičné havárie (24. 10. 2026)

VM smazaná v konzoli i s diskem → `tofu apply` → Tailscale IP + `DEPLOY_KNOWN_HOSTS` → Deploy → `restore.sh latest`.
Objednávky vytvořené před poslední zálohou jsou zpět se stejnými čísly. Samotná obnova dat (`restore.sh`) trvala
přibližně 5 minut (nestopováno přesně). Celkové RTO tvoří hlavně čekání: vytvoření VM (~1 min), cloud-init
(~5–8 min), ruční kroky v Tailscale a GitHubu, nasazení (~1 min) a obnova.

Co by RTO zkrátilo: pevný otisk SSH serveru (odpadne `DEPLOY_KNOWN_HOSTS`), Tailscale OAuth klient pro VM
(odpadne ruční auth key a úprava IP), obnova spouštěná automaticky po prvním nasazení na prázdnou VM.
Pro výukový projekt s RPO 24 h je ruční postup přijatelný – a hlavně je teď vyzkoušený.

## Známá omezení

- **RPO až 24 h** – před rizikovou akcí (změna schématu, ruční zásah) spusťte zálohu ručně (F1).
- **Moduly se zálohují s odstupem sekund** – sága rozběhnutá právě v tu chvíli může po obnově viset;
  ukončí ji timeout ságy (kompenzace, krok 9 EDA). Zprávy, které byly v tu chvíli jen v RabbitMQ, se ztratí.
- **Změna schématu** (`EnsureCreated`, bez migrací) – záloha starého schématu se do nové verze aplikace neobnoví
  bez ručního zásahu. Řešení: EF Core migrace (viz *Volitelně: kam dál* v roadmapě).
- **`*.before-restore-…` se nemažou samy** – po ověření obnovy je smažte ručně.
- **Obnova je ruční příkaz** – automatickou obnovu nová VM nedělá (záměr: obnova starých dat je rozhodnutí člověka).
- **Zálohy nejsou šifrované naším klíčem** – jen šifrováním Object Storage (klíče spravuje Oracle).
- **Jen jeden region** – výpadek celého Frankfurtu zálohy nezachrání.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| `ssh minishop-vm "systemctl list-timers minishop-backup.timer --no-pager"` | další běh v 02:30 UTC |
| `ssh minishop-vm "journalctl -u minishop-backup -n 15 --no-pager"` | `Záloha nahrána: …` |
| `ssh minishop-vm "sudo -u deploy /opt/minishop/restore.sh list"` | seznam záloh |
| OCI → *Buckets* → `minishop-backups` → *Lifecycle policy rules* | `smazat-zalohy-po-7-dnech`, Enabled |
| healthchecks.io | `minishop-backup` Up, poslední ping z noci |
| `tofu plan -destroy` | chyba *prevent_destroy* u bucketu i reserved IP (nic se nesmaže) |
| Cvičná havárie (G) | objednávky zpět, RTO změřené |

## Vyzkoušejte sami

1. **VM nesmí mazat.** Na VM v kontejneru `oci-cli` zkuste
   `oci os object delete --auth instance_principal -bn minishop-backups --name <záloha> --force` → `NotAuthorizedOrNotFound`.
2. **Hlídač zálohy.** `ssh minishop-vm "sudo systemctl disable minishop-backup.timer"` → druhý den ráno e-mail
   z healthchecks.io (*DOWN*). Pak `enable --now`.
3. **Poškozená záloha se neobnoví.** Představte si, že archiv obsahuje rozbitou DB – `restore` ji zastaví na
   `integrity_check` ještě předtím, než cokoli vymění (kód v `backup.py`, funkce `restore`).
4. **Obnova konkrétní zálohy.** `restore.sh list` → `restore.sh minishop-…tar.gz` (návrat o den zpět).
5. **EDA po obnově.** Objednávka rozpracovaná v okamžiku zálohy (*Pending*) po obnově buď doběhne (zprávy čekaly
   v outboxu), nebo ji zruší timeout ságy – nikdy nezůstane navždy viset.
