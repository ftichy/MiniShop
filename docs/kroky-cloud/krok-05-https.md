# Cloud krok 5: Doména, HTTPS a zámek (Caddy)

Po kroku 4 běžel MiniShop na holé IP přes HTTP a byl vidět jen z vaší domácí IP. Teď dostane jméno
(`minishop-tichy.duckdns.org`), šifrované spojení s certifikátem od Let's Encrypt a heslo – a může být otevřený
celému internetu. Kód aplikace se nemění; před Web se postaví reverse proxy **Caddy**.

## Jak to funguje

Přirovnání: Caddy je **recepce u vchodu do budovy**. Návštěvníky pouští jen hlavním vchodem (HTTPS), u každého
zkontroluje průkaz (heslo) a teprve pak ho pošle do kanceláře MiniShopu. Do kanceláře už žádné jiné dveře
z ulice nevedou. Úředně ověřenou cedulku „tohle je opravdu minishop-tichy.duckdns.org“ (certifikát) si recepce
sama vyřídí u Let's Encrypt a sama ji každé ~2 měsíce obnoví.

```
prohlížeč ── https://minishop-tichy.duckdns.org
     │ 1. DNS (DuckDNS): minishop-tichy.duckdns.org → 141.144.255.238 (reserved IP)
     ▼
[security list: 80, 443 z celého internetu; 22 jen z domácí IP]
     ▼
VM ── Caddy :80  → 308 přesměrování na https (a odpovědi pro ověření Let's Encrypt)
      Caddy :443 → TLS (certifikát z volume caddy-data) → heslo (basic auth) → 401 bez něj
                     │ X-Forwarded-Proto: https, X-Forwarded-For: <IP klienta>
                     ▼
               web:8080 (jen v síti Compose, ven nepublikovaný) → inventory-api, rabbitmq
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **DNS** | telefonní seznam: jméno → IP | DuckDNS, záznam A `minishop-tichy` → `141.144.255.238` |
| **Reserved IP** | veřejná IP, která patří vám, ne VM | `minishop-ip`; přežije smazání a nové vytvoření VM (kroky 9, 12) |
| **TLS certifikát** | ověřená „občanka“ serveru + klíč k šifrování | Let's Encrypt, 90 dní, obnovuje Caddy |
| **ACME / HTTP-01** | jak Let's Encrypt ověří, že doménu ovládáte | stáhne z `http://<doména>/.well-known/acme-challenge/…` soubor, který připravil Caddy |
| **Reverse proxy** | server, který přijímá požadavky za jiný server | Caddy → `web:8080` |
| **Basic auth** | jméno + heslo v hlavičce každého požadavku (okno prohlížeče) | uživatel `minishop`, v `.env` jen bcrypt otisk |

**Proč musí být port 80 otevřený celému internetu**, i když web jede na 443: Let's Encrypt ověřuje doménu
ze **5 míst na světě** najednou (v logu Caddy uvidíte 5 různých IP) – kdyby port 80 pouštěl jen vaši IP,
certifikát by nedostal. Na portu 80 Caddy jinak jen přesměruje na https.

**Proč heslo na proxy a ne v aplikaci:** MiniShop nemá uživatele. Zámek na proxy je nejrychlejší pojistka proti
botům, kteří veřejné IP skenují během minut. Až aplikace dostane vlastní přihlašování, vypne se jedním řádkem
v `.env` (`BASIC_AUTH=off`) – doména, HTTPS i Caddy zůstanou (přihlašovací cookie stejně vyžadují HTTPS).

**Jak Web pozná, že klient přišel přes HTTPS:** mezi Caddy a Webem jde obyčejné HTTP. Caddy ale přidá hlavičky
`X-Forwarded-Proto: https` a `X-Forwarded-For`. Web jim věří díky `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
(nastaveno už v kroku 2) – `UseHttpsRedirection` proto nepřesměrovává v kruhu a logy ukazují IP klienta.

**Proč doplňkový soubor `compose.cloud.yaml`:** lokálně (krok 2) má zůstat `http://localhost:8080` bez Caddy.
Compose umí soubory skládat: druhý doplní službu `caddy` a u `web` smaže publikovaný port (`ports: !reset []`).
Na VM to zapíná `COMPOSE_FILE=compose.yaml:compose.cloud.yaml` v `.env`, takže příkazy zůstávají stejné.

## Návod krok za krokem

### A. Reserved IP (konzole OCI)

- [ ] **A1.** Detail VM `minishop-vm` (vyhledávání nahoře nebo *Compute → Instances*, compartment `minishop`) →
  záložka **Networking** → v tabulce *Attached VNICs* odkaz **minishop-vm** (*Primary VNIC*).
  Sekci *Quick actions → Connect* nepoužívat (vytvořila by síťové zdroje navíc).
- [ ] **A2.** Záložka **IP administration** → řádek `10.0.0.x (Primary IP)` → **⋮ → Edit** →
  *Public IP type*: **No public IP** → *Update*. (VM je teď chvíli z internetu nedostupná.)
- [ ] **A3.** Znovu **⋮ → Edit** → **Reserved public IP** → *Create new* – Name `minishop-ip`, compartment
  `minishop` → *Update*. Sloupec *Public IP address* ukáže novou IP s „(Reserved)“. 📝
  (*IP lifetime: Ephemeral* v tabulce patří privátní IP, ne veřejné.)
- [ ] **A4.** V `~/.ssh/config` přepsat `HostName` na novou IP; první `ssh minishop-vm` přidá nový záznam
  do `known_hosts` (otisk serveru je stejný – mění se jen adresa).

### B. DuckDNS

- [ ] **B1.** https://www.duckdns.org → přihlásit (GitHub/Google) → *sub domain* `minishop-tichy` → **add domain**.
- [ ] **B2.** *current ip* přepsat na **reserved IP z A3** (DuckDNS předvyplní IP vašeho PC!) → **update ip**.
- [ ] **B3.** Ověření: `nslookup minishop-tichy.duckdns.org 1.1.1.1` → reserved IP.

**Token** nahoře na stránce DuckDNS je heslo k doméně – nikam ho nekopírovat. Nepotřebujeme ho: reserved IP
se nemění, takže není potřeba ani automatická aktualizace IP.

### C. Caddy na VM

- [ ] **C1.** Z PC (kořen repozitáře) zkopírovat nové soubory:

  ```powershell
  scp deploy/Caddyfile deploy/compose.cloud.yaml deploy/set-basic-auth.sh minishop-vm:minishop/
  ```

- [ ] **C2.** Na VM (`ssh minishop-vm`) doplnit `.env`:

  ```bash
  cd ~/minishop
  sed -i 's/\r$//' Caddyfile compose.cloud.yaml set-basic-auth.sh   # konce řádků z Windows
  cat >> .env <<EOF
  COMPOSE_FILE=compose.yaml:compose.cloud.yaml
  DOMAIN=minishop-tichy.duckdns.org
  BASIC_AUTH=on
  BASIC_AUTH_USER=minishop
  EOF
  docker compose config --services     # → rabbitmq, inventory-api, web, caddy
  ```

  Pozor: na Linuxu se soubory v `COMPOSE_FILE` oddělují `:`, na Windows `;`.
- [ ] **C3.** Heslo (zadáváte jen vy, skrytě) a spuštění:

  ```bash
  bash set-basic-auth.sh
  ```

  Skript heslo převede na bcrypt otisk (`caddy hash-password`), uloží do `.env` jako `BASIC_AUTH_HASH='…'`
  a spustí `docker compose up -d` – Caddy naběhne a Web přestane být publikovaný ven.
  Změna hesla později = spustit skript znovu.

### D. Security list (konzole OCI) – až po C

- [ ] **D1.** *Default Security List for minishop-vcn* → pravidlo pro port **80** → *Edit* → Source CIDR
  **`0.0.0.0/0`**, popis „HTTP – Caddy (Let's Encrypt + přesměrování na HTTPS)“.
- [ ] **D2.** *Add Ingress Rules* → `0.0.0.0/0`, TCP, Destination Port **443**, popis „HTTPS – Caddy“.
- [ ] **D3.** Pravidlo pro **22** nechat jen z vaší IP.

Pořadí C → D je schválně: když se porty otevřou světu, vede port 80 už jen do Caddy se zámkem.
Port 443 v iptables na VM je otevřený od kroku 4 (E2).

### E. Ověření

- [ ] **E1.** Log Caddy: `docker compose logs caddy | grep -E "served key|certificate obtained"` →
  5× *served key authentication* (různé IP Let's Encrypt) a *certificate obtained successfully*.
- [ ] **E2.** Prohlížeč: `https://minishop-tichy.duckdns.org` → zámek v adresním řádku, okno pro jméno a heslo,
  po přihlášení MiniShop. Objednávka → Confirmed, průběh objednávky se zobrazí.
- [ ] **E3.** `http://minishop-tichy.duckdns.org` → přesměruje na `https://`.
- [ ] **E4.** Z mobilu přes mobilní data → funguje (s heslem). V kroku 4 to schválně nešlo.

## Nové soubory

| Soubor | Obsah |
| --- | --- |
| `deploy/Caddyfile` | doména z `DOMAIN`, vypínatelný `basic_auth` (`BASIC_AUTH=on/off`), `reverse_proxy web:8080`, jen HTTP/1+2 |
| `deploy/compose.cloud.yaml` | doplněk pro VM: služba `caddy` (80, 443, volumes `caddy-data`/`caddy-config`), `web` bez publikovaného portu |
| `deploy/set-basic-auth.sh` | skryté zadání hesla → bcrypt otisk do `.env` → `docker compose up -d` |
| `docs/kroky-cloud/krok-05-https.md` | tento návod |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/.env.example` | vzor proměnných pro VM: `COMPOSE_FILE`, `DOMAIN`, `BASIC_AUTH`, `BASIC_AUTH_USER`, `BASIC_AUTH_HASH` |

Mimo repozitář: na VM `~/minishop/.env` (nové řádky), v OCI reserved IP `minishop-ip`, upravený security list,
na DuckDNS doména `minishop-tichy`.

## Rozhodnutí v tomto kroku

- **DuckDNS místo vlastní domény** – 0 Kč, hotové za minutu; pro výuku stačí. Vlastní doména jde doplnit
  později (jen nový DNS záznam a `DOMAIN` v `.env`).
- **Reserved IP hned teď** – dřív, než se adresa zapíše do DNS. Ephemeral IP by se změnila při novém vytvoření VM.
- **Caddy, ne Nginx + certbot** – certifikát, obnova a přesměrování na HTTPS bez jediného řádku konfigurace navíc.
- **Heslo na proxy, vypínatelné proměnnou** – do doby, než aplikace dostane uživatele a role. Snippety
  `(auth-on)`/`(auth-off)` a `import auth-{$BASIC_AUTH:on}`: Caddy dosadí proměnnou ještě před čtením souboru.
- **Do `.env` jen bcrypt otisk** v jednoduchých uvozovkách – otisk obsahuje `$`, které by Compose jinak
  vykládal jako proměnné. Heslo nikdy neopustí terminál, ve kterém ho zadáváte.
- **Jen HTTP/1.1 a HTTP/2** (`protocols h1 h2`) – HTTP/3 by potřeboval UDP 443 v security listu i iptables.
- **`compose.cloud.yaml` jako doplněk** – lokální běh (krok 2) zůstává beze změny.
- **Port 80 otevřený všem** – nutné pro ověření Let's Encrypt (HTTP-01); jinak jen přesměruje na HTTPS.
- **SSH dál jen z domácí IP** – zavření SSH úplně (Tailscale) z plánu odloženo.

## Známá omezení

- **Jedno heslo pro všechny**, bez uživatelů a rolí. Basic auth posílá heslo s každým požadavkem (přes HTTPS
  šifrovaně); prohlížeč ho drží do zavření. Odhlášení není.
- **Závislost na DuckDNS** (bezplatná služba bez záruky). Výpadek DuckDNS = doména nefunguje, IP ano.
- **Certifikát obnovuje Caddy** – volume `caddy-data` se nesmí mazat (`docker compose down -v`!), jinak
  žádá o nový a při opakování narazí na limity Let's Encrypt (týdně omezený počet certifikátů na doménu).
- **Proměnná `WEB_PORT` v `.env` na VM už nemá vliv** (Web není publikovaný); zůstala z kroku 4.
- **Změna domácí IP** stále zablokuje SSH (port 22) – web už ne.
- **Bez HSTS a bezpečnostních hlaviček** (CSP, X-Frame-Options…). Caddy je umí přidat (`header`), řešíme
  případně v kroku 10/11.
- **Log požadavků Webu** vidí všechny požadavky jako z Caddy, pokud nefungují forwarded headers – fungují,
  ale jen díky `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; při jeho odstranění by se pokazily i přesměrování.

## Jak ověřit

| Kontrola | Očekávaný výsledek |
| --- | --- |
| `nslookup minishop-tichy.duckdns.org 1.1.1.1` | reserved IP |
| `curl -I http://minishop-tichy.duckdns.org` | `308` → `https://…` |
| `curl -I https://minishop-tichy.duckdns.org` | `401` (bez hesla) |
| `curl -I -u minishop:<heslo> https://minishop-tichy.duckdns.org` | `200` |
| `http://<reserved IP>` | `308` – i holá IP vede do Caddy, Web přímo vidět není |
| Certifikát (zámek v prohlížeči) | vydavatel Let's Encrypt, platnost 90 dní |
| `docker compose ps` na VM | 4 služby; `web` bez `0.0.0.0:…`, `caddy` s `0.0.0.0:80`, `0.0.0.0:443` |
| Mobil přes mobilní data | MiniShop po zadání hesla |

## Vyzkoušejte sami

1. **Kdo klepe na dveře.** Po pár hodinách `docker compose logs caddy | grep -c '"status":401'` nebo
   `docker compose logs web | grep -E 'wp-login|\.env|phpmyadmin'` – boti zkoušejí známé díry. Díky heslu
   se k Webu nedostanou (401 vrací už Caddy).
2. **Certifikát zblízka.** V prohlížeči klik na zámek → certifikát: vydavatel, platnost, řetěz až ke kořenové
   autoritě. Nebo `openssl s_client -connect minishop-tichy.duckdns.org:443 -servername minishop-tichy.duckdns.org`.
3. **Vypnutí zámku.** Na VM `sed -i 's/^BASIC_AUTH=on/BASIC_AUTH=off/' .env && docker compose up -d caddy` →
   web bez hesla (jen na chvíli!). Vrátit `on`. Takhle se zámek vypne, až aplikace dostane vlastní přihlašování.
4. **Kam vedou hlavičky.** `docker compose logs web | grep "HTTP GET /Orders"` – IP klienta je vaše, ne Caddy
   (díky `X-Forwarded-For`).
5. **Reserved IP přežije VM.** (Jen pro odvážné, nebo až v kroku 9.) Po smazání a novém vytvoření VM lze stejnou
   IP připojit k nové VM – DNS záznam se nemusí měnit.
