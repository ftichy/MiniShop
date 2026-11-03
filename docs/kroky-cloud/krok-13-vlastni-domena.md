# Cloud krok 13: Vlastní doména minishop-tichy.cz

MiniShop běžel na `minishop-tichy.duckdns.org`. Technicky to funguje, ale antiviry, firemní filtry a některé
prohlížeče adresu blokují nebo před ní varují. Nejde o MiniShop samotný: na sdílených doménách zdarma (DuckDNS,
No-IP…) si kdokoli za minutu založí adresu a podvodníci toho hojně využívají. Reputační služby proto často hodnotí
celou doménu `duckdns.org` a s ní i nás. Řešením je **vlastní doména** `minishop-tichy.cz` (Wedos). Stará adresa
zůstává a **trvale přesměruje** na novou.

## Jak to funguje

Přirovnání: firma se přestěhovala z „pavlače“ sdílené s neznámými nájemníky do vlastního domu. Na staré adrese
nechá na dveřích ceduli „přestěhovali jsme se sem“ (přesměrování 301), takže dopisy a staré vizitky dál dojdou.

```
 prohlížeč ── https://minishop-tichy.cz
     │ 1. DNS (WEDOS, ns.wedos.*): A @ → 141.144.255.238, A dashboard → 141.144.255.238 (TTL 300, žádné AAAA)
     ▼
 reserved IP ── security list 80/443 ── Caddy
     │ 2. certifikát Let's Encrypt pro minishop-tichy.cz a dashboard.minishop-tichy.cz (Caddy sám, HTTP-01 na portu 80)
     ├─ minishop-tichy.cz                 → web:8080 (/health, /health/inventory jako dřív)
     ├─ dashboard.minishop-tichy.cz       → aspire-dashboard:18888 (basic auth)
     ├─ minishop-tichy.duckdns.org        → 301 https://minishop-tichy.cz{cesta}
     └─ dashboard.minishop-tichy.duckdns.org → 301 https://dashboard.minishop-tichy.cz{cesta}
```

| Pojem | Co to je | V MiniShopu |
| --- | --- | --- |
| **Registrátor** | firma, přes kterou si doménu pronajímáte u registru (CZ.NIC pro `.cz`) | Wedos, roční poplatek |
| **Nameservery (NS)** | servery, které o doméně odpovídají na DNS dotazy | `ns.wedos.cz/.com/.net/.eu` (DNS hosting k doméně zdarma) |
| **Záznam A / AAAA** | jméno → IPv4 / IPv6 adresa | dvě A na reserved IP; AAAA žádné (VM nemá IPv6) |
| **TTL** | jak dlouho si ostatní servery odpověď pamatují | 300 s, změna se projeví do 5 minut |
| **Přesměrování 301** | „trvale přestěhováno“, prohlížeč i vyhledávače si novou adresu zapamatují | Caddy, `redir … permanent` |

**Proč smazat výchozí AAAA:** Wedos novou doménu „zaparkuje“ – A i AAAA ukazují na jeho reklamní stránku.
Kdyby AAAA zůstal, prohlížeče s IPv6 by šly na Wedos místo na VM a Let's Encrypt (zkouší IPv6 jako první)
by certifikát nevydal.

## Návod krok za krokem

**A. Registrace (uživatel, Wedos)**

- [x] **A1.** https://www.wedos.cz → hledat `minishop-tichy.cz` → do košíku jen doménu (bez hostingu, e-mailu, SSL).
- [x] **A2.** DNS servery nechat výchozí WEDOS, držitel = fyzická osoba, potvrdit e-mail od CZ.NIC.
- [x] **A3.** Zaplatit, počkat na stav *aktivní*.

**B. DNS záznamy (uživatel, client.wedos.com → DNS → minishop-tichy.cz → DNS záznamy)**

- [x] **B1.** Smazat všechny výchozí A a AAAA (parkování `185.8.237.22`, `2a0e:acc0::d22`, i `*` a `www`).
- [x] **B2.** Nový záznam: jméno prázdné, A, `141.144.255.238`, TTL 300. Totéž se jménem `dashboard`.
- [x] **B3.** **Aplikovat změny** (bez toho Wedos změny pošle až do 5 minut).
- [x] **B4.** Ověření: `nslookup minishop-tichy.cz 1.1.1.1` a `nslookup -type=AAAA minishop-tichy.cz 1.1.1.1`
      → jen `141.144.255.238`, žádná IPv6. Totéž pro `dashboard.minishop-tichy.cz`.

**C. Kód (PR, nasazení až po B4)**

- [x] **C1.** PR s touto změnou → zelené CI → sloučit. Deploy nastaví `DOMAIN` a `LEGACY_DOMAIN`, Caddy po `caddy reload`
      získá certifikáty pro nová jména, smoke test kontroluje novou doménu i přesměrování ze staré.

**D. Po nasazení (uživatel)**

- [x] **D1.** UptimeRobot: v obou monitorech změnit URL na `https://minishop-tichy.cz/health` a `…/health/inventory`
      (přesměrování by sice sledovaly, ale výpadek staré adresy by hlásil zbytečně).
- [ ] **D2.** Přihlásit se znovu – cookie přihlášení patří k jménu domény, na nové adrese je uživatel odhlášený.
- [ ] **D3.** Záložky, odkaz v profilu na GitHubu apod. přepsat na novou adresu.

## Nové soubory

| Soubor | K čemu |
| --- | --- |
| `docs/kroky-cloud/krok-13-vlastni-domena.md` | tento souhrn |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/production.env` | `DOMAIN=minishop-tichy.cz`, nové `LEGACY_DOMAIN=minishop-tichy.duckdns.org` |
| `deploy/compose.cloud.yaml` | Caddy dostane `LEGACY_DOMAIN` (povinné) |
| `deploy/Caddyfile` | weby `{$LEGACY_DOMAIN}` a `dashboard.{$LEGACY_DOMAIN}` → `redir … permanent` |
| `.github/workflows/deploy.yml` | odkaz environmentu a smoke test na nové doméně + kontrola 301 ze staré adresy |
| `infra/variables.tf`, `infra/compute.tf` | výchozí doména pro výpis a komentář |
| `src/MiniShop.Web/Program.cs`, `Accounts/AccountEmails.cs` | jen komentáře |
| příručka (`https-a-pristup`, `nasazeni/index`, `monitoring`, `treti-strany/sluzby`, `historie`), `docs/secrets.md`, `docs/Cloud-roadmap.md`, `docs/Ucty-roadmap.md`, `CLAUDE.md` | nová adresa, Wedos, krok 13 |

## Rozhodnutí v tomto kroku

- **Wedos jako registrátor i DNS** – česká firma, DNS k doméně zdarma, záznamy jde měnit hned. DNS jinde
  (např. Cloudflare) by přineslo víc funkcí, ale i další účet a nameservery navíc – pro dva záznamy zbytečné.
- **Záznamy ručně, ne v OpenTofu** – Wedos nemá oficiálního providera; dva záznamy, které se mění jednou za roky,
  nestojí za vlastní automatizaci. Reserved IP má `prevent_destroy`, takže se pod nimi nezmění.
- **Žádné `www`** – jedna kanonická adresa. `www` jde přidat později (záznam A + jméno v Caddyfile).
- **Stará adresa přesměrovává, nemizí** – odkazy z dříve odeslaných e-mailů (potvrzení účtu, reset hesla) nesou
  starou adresu; 301 zachová cestu i parametry, takže token v odkazu dál platí.
- **Přesměrování dělá Caddy, ne aplikace** – Web o doménách nic neví (odkazy skládá z aktuálního požadavku).
- **HSTS dál bez preload a subdomén** – preload jde vrátit jen velmi pomalu; nic nezískáme.
- **Smoke test zná doménu natvrdo** (env kroku) – workflow nečte `production.env`; při další změně domény upravit obojí.

## Známá omezení

- Doména stojí peníze a **musí se prodlužovat** – propadlá doména = nedostupný web (a jméno si může koupit kdokoli).
  Automatické prodloužení u Wedosu zapnout, kontakt v CZ.NIC udržovat platný.
- Čerstvě registrované domény některé filtry prvních pár týdnů hodnotí opatrně („newly registered domain“);
  časem se pověst ustálí.
- Stará adresa závisí dál na DuckDNS – když tam záznam zmizí, přestane fungovat jen přesměrování.
- Uživatelé jsou po přechodu odhlášení (cookie patří k doméně); hesla ani data se nemění.

## Jak ověřit

| Příkaz / adresa | Očekávaný výsledek |
| --- | --- |
| `nslookup minishop-tichy.cz 1.1.1.1` | jen `141.144.255.238` |
| `nslookup -type=AAAA minishop-tichy.cz 1.1.1.1` | žádná adresa |
| `curl -s -o /dev/null -w '%{http_code}' https://minishop-tichy.cz/` | `200` (platný certifikát) |
| `curl https://minishop-tichy.cz/health` | `Healthy` |
| `curl -s -o /dev/null -w '%{http_code}' https://dashboard.minishop-tichy.cz/` | `401` |
| `curl -sI https://minishop-tichy.duckdns.org/Shop?q=x` | `301`, `Location: https://minishop-tichy.cz/Shop?q=x` |
| GitHub → *Deployments* → `production` | odkaz na https://minishop-tichy.cz |

## Vyzkoušejte sami

1. Otevřete starou adresu https://minishop-tichy.duckdns.org/Shop – v F12 → *Network* uvidíte `301` a pak
   stránku z nové domény.
2. `nslookup minishop-tichy.cz ns.wedos.cz` (ptáte se přímo autoritativního serveru) vs. `nslookup … 8.8.8.8`
   (cache Googlu) – po změně záznamu chvíli vrací každý něco jiného; to je TTL v praxi.
3. Zkontrolujte certifikát (zámek v adresním řádku): vydavatel Let's Encrypt, jméno `minishop-tichy.cz`.
