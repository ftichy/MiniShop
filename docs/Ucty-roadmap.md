# MiniShop: uživatelské účty a role

26. 10. 2026 · František

MiniShop dnes nezná uživatele. Kdokoli (za basic auth v Caddy) vidí všechny objednávky, zakládá produkty a naskladňuje.
V 10 malých krocích přidáme účty, dvě role a pravidla, kdo co smí. Po každém kroku aplikace funguje a nasadí se
do cloudu (CD od cloudového kroku 8 nasazuje každé sloučení do `main`), takže každý krok obsahuje i svou cloudovou část
(databáze na volume, záloha, secrets). Dokument je zatím jen plán, do kódu se nic nemění.

## Zadání

### Role a oprávnění

| Akce | Návštěvník | User | Admin |
| --- | :-: | :-: | :-: |
| Registrace (sám sebe) | ✅ | – | – |
| Přihlášení / odhlášení | ✅ | ✅ | ✅ |
| Založit objednávku | ❌ | ✅ | ❌ |
| Vidět objednávky | ❌ | jen své | všechny |
| Zrušit objednávku ve stavu Pending nebo Confirmed | ❌ | jen svou | jakoukoli |
| Vyřídit objednávku (Confirmed → Completed) | ❌ | ❌ | ✅ |
| Produkty: přidat, naskladnit, upravit, smazat; obnovit katalog | ❌ | ❌ | ✅ |
| Průběh a přehrání objednávky (Journeys) | ❌ | ❌ | ✅ |
| Přehled uživatelů a jejich objednávek | ❌ | ❌ | ✅ |
| Povýšit uživatele na admina, zablokovat účet | ❌ | ❌ | ✅ |
| Příručka `/Docs` | ✅ | ✅ | ✅ |

Zrušit nejde objednávka Zamítnutá (Rejected), Vyřízená (Completed) ani už Zrušená (Cancelled).

### Účty

- User se registruje sám (e-mail + heslo, později i Google účet).
- Admina může vytvořit jen admin – povýšením existujícího uživatele.
- První admin vznikne bezpečným „bootstrapem“ – v produkci příkazem `create-admin` na serveru (krok 2).
- E-maily (potvrzení adresy, zapomenuté heslo, stav objednávky) se odesílají skutečně a zdarma (krok 5).
- Basic auth v Caddy zůstává, dokud účty nejsou zpevněné; vypne se v kroku 9 (dashboard ho ponechá).

## Úvod: autentizace a autorizace

Dvě otázky, které se často pletou:

- **Autentizace** – *kdo jsi?* Přihlášení heslem nebo přes Google. Výsledek je **identita** (uživatel s ID a rolemi),
  kterou si prohlížeč nese v **cookie**.
- **Autorizace** – *co smíš?* Rozhodnutí podle role (Admin smí naskladnit) nebo podle vlastnictví (User smí vidět
  objednávku, jen když je jeho).

Přirovnání: na recepci hotelu ukážete občanku (autentizace) a dostanete kartu od pokoje. Karta pak otevře jen váš pokoj
a posilovnu, ne kancelář ředitele (autorizace). Recepce kartu vydá jednou, dveře ji kontrolují pokaždé.

### Základní pojmy

| Pojem | Význam | Příklad v MiniShopu |
| --- | --- | --- |
| ASP.NET Core Identity | Hotová knihovna na účty: hashování hesel, role, zablokování, reset hesla, externí přihlášení | `UserManager`, `SignInManager` |
| Claim | Jeden údaj o přihlášeném uživateli uložený v cookie | `sub` = ID uživatele, `role` = `Admin` |
| Role | Pojmenovaná skupina oprávnění | `User`, `Admin` |
| Policy | Pojmenované pravidlo autorizace, které se testuje na jednom místě | `RequireAdmin`, `RequireCustomer` |
| Resource-based authorization | Rozhodnutí podle konkrétního záznamu, ne jen role | „objednávka 42 patří přihlášenému“ |
| IDOR | Chyba: k cizím datům se dostanu jen změnou čísla v URL | `/Orders/Journey/43` – brání krok 3 |
| Lockout | Dočasné zablokování po opakovaném špatném hesle | 5 pokusů → 15 minut |
| Security stamp | „Verze“ účtu; po změně hesla zneplatní staré cookie | odhlášení na všech zařízeních |
| Bootstrap admina | Jak bezpečně vznikne první admin, když ho nemá kdo jmenovat | příkaz `create-admin` na serveru |
| OAuth / OpenID Connect | Přihlášení přes jiného poskytovatele, který ověří identitu | „Přihlásit přes Google“ |

## Best practice, kterých se držíme

1. **Vlastní kryptografii a přihlašování nepsat** – ASP.NET Core Identity (PBKDF2, lockout, security stamp, Google).
2. **Autorizace na serveru, ne skrýváním v UI.** Policies na stránkách a API; data se filtrují podle uživatele
   přímo v dotazu (`OrderService`), ne až v šabloně.
3. **Cizí objednávka = 404, ne 403** – útočník nepozná, že existuje (ochrana proti IDOR).
4. **Nejmenší nutná oprávnění a oddělení povinností** – admin spravuje obchod, ale neobjednává.
5. **Bezpečný bootstrap** – první admin jen když žádný admin neexistuje: v produkci příkazem na serveru (heslo
   nikde neleží), ve vývoji ze známého hesla s vynucenou změnou.
   Ne „první registrovaný je admin“ (na veřejném webu vyhraje kdokoli rychlejší).
6. **Hesla podle NIST** – délka (≥ 10 znaků) místo povinných speciálních znaků.
7. **Ochrana přihlášení** – lockout, rate limiting, stejná hláška pro neznámý e-mail i špatné heslo.
8. **Cookie** `HttpOnly`, `Secure`, `SameSite=Lax`, antiforgery, odhlášení jen POSTem. Perzistentní Data Protection
   klíče (cloud krok 1) zajistí, že přihlášení přežije nasazení.
9. **API vrací 401/403**, nepřesměrovává na přihlašovací stránku.
10. **Zero trust mezi službami** – Inventory.Api nevěří každému, kdo se dostane do sítě (krok 7).
11. **Osobní údaje mimo logy a trasy** – do spanu `user.id`, nikdy e-mail, heslo ani token. Bezpečnostní události
    (neúspěšné přihlášení, změna role) do auditu.
12. **Externí přihlášení = další metoda k jednomu účtu**, párování jen přes ověřený e-mail, role spravuje MiniShop.

## Jak to zapadá do architektury

- Nový modul **Accounts** (stejný vzor jako Journeys): vlastní `users.db`, hostuje ho Web, bez outboxu.
  Ordering o Accounts neví (hlídá `ModuleBoundariesTests`) – zná jen ID uživatele (`CustomerId`) z cookie,
  které mu předá Web.
- Objednávka si jméno a e-mail zákazníka **kopíruje v době objednání** (jako `CatalogProduct` kopíruje produkt).
  Změna profilu tak nepřepíše historii a Ordering se nemusí ptát jiného modulu.
- Kontrakty (`OrderPlacedV1` …) se nemění – sklad uživatele nepotřebuje.
- Zrušení objednávky ve stavu Pending je **nový přechod ságy** – nejzajímavější EDA část plánu (krok 4).

## Přehled kroků

| Krok | Fáze | Co přinese | Nový nástroj | Cena |
| --- | --- | --- | --- | --- |
| 1. Účty, registrace, přihlášení | A – základ | Uživatel se zaregistruje a přihlásí, `users.db` | ASP.NET Core Identity | 0 |
| 2. Role a první admin | A | Role User/Admin, policies, bootstrap admina, zamčené admin akce | policies, `WebApplicationFactory` | 0 |
| 3. Vlastník objednávky | A | User vidí jen své objednávky, admin neobjednává | resource-based authorization | 0 |
| 4. Zrušení objednávky (i Pending) – **odloženo** | B – EDA | Zrušení během rezervace, kompenzace ságou | – | 0 |
| 5. Skutečné e-maily | B | Potvrzení e-mailu, reset hesla, e-mail o objednávce | MailKit, Mailpit, SMTP | 0 |
| 6. Administrace uživatelů | C – správa | Přehled uživatelů, povýšení, blokace, audit | – | 0 |
| 7. Zabezpečení skladu | C | Inventory.Api přijme zápis jen od Webu | servisní klíč | 0 |
| 8. Zpevnění | D – provoz | Lockout, rate limiting, bezpečnostní hlavičky | `RateLimiter`, Caddy | 0 |
| 9. Otevření webu | D | Vypnutý basic auth, ochrana registrace, smazání účtu | Cloudflare Turnstile (volitelně) | 0 |
| 10. Přihlášení přes Google | D | „Přihlásit přes Google“ | Google OAuth | 0 |

---

## Fáze A: účty a role

## Krok 1: Účty, registrace, přihlášení

**Teorie.** Heslo se nikdy neukládá – ukládá se jeho **hash se solí** (jednosměrný otisk), a při přihlášení se porovnají
otisky. Po úspěšném přihlášení server vydá **zašifrovanou cookie** s claims (ID, e-mail, role). Prohlížeč ji posílá
s každým požadavkem, server ji jen dešifruje – do databáze se nedívá.

**V MiniShopu:**

- Modul Accounts: `Infrastructure/Accounts` (`AccountsDbContext` z `IdentityDbContext`, `ApplicationUser` se jménem,
  `users.db`, `AccountsDatabaseInitializer`), registrace Identity ve Webu.
- České stránky `Account/Register`, `Account/Login`, odhlášení (POST); v hlavičce „Přihlášen jako …“.
- Pravidla hesla (≥ 10 znaků, bez povinných speciálních znaků), unikátní e-mail, nastavení cookie;
  `/api/*` vrací 401 místo přesměrování.
- Zatím se nic nezamyká – jen víme, kdo je přihlášený.
- Cloud: `ConnectionStrings__Accounts=Data Source=/data/users.db`, `users.db` do `backup.py`.

**Hotovo, když:** registrace a přihlášení fungují lokálně i v cloudu, `users.db` je v noční záloze, testy procházejí.

## Krok 2: Role a první admin

**Teorie.** Role je štítek na uživateli; **policy** je pravidlo („jen Admin“), které se pojmenuje jednou a používá
všude. Bootstrap admina řeší problém slepice a vejce: admina jmenuje admin – ale kdo jmenuje prvního?

**V MiniShopu:**

- Role `User` (dostane každý při registraci) a `Admin`; policies `RequireAdmin`, `RequireCustomer`.
- První admin (jen když žádný neexistuje): v produkci příkazem
  `docker compose exec web dotnet MiniShop.Web.dll create-admin --email …` (heslo se píše do terminálu, žádný secret);
  ve vývoji seed z `AdminBootstrap` v `appsettings.Development.json` s příznakem „musí změnit heslo“.
  *(Původně plánovaný secret `ADMIN_BOOTSTRAP_PASSWORD` jsme nahradili příkazem – admin vzniká jednou za život
  aplikace a `users.db` je v záloze.)*
- Zamčené pro admina: stránka Produkty, obnova katalogu, vyřízení objednávky, Průběh a Přehrání objednávky
  (stránky i API skladu ve Webu).
- Menu podle role, stránka „Přístup odepřen“.

**Hotovo, když:** test matice oprávnění (role × akce) prochází, admin založený příkazem se v cloudu přihlásí
a vidí Produkty, běžný uživatel ne.

## Krok 3: Vlastník objednávky

**Teorie.** Role nestačí – „User smí vidět objednávky“ neznamená „všechny“. Rozhoduje vztah k záznamu
(resource-based authorization). Filtrovat se musí v dotazu do databáze, jinak stačí uhodnout číslo (IDOR).

**V MiniShopu:**

- `Order.CustomerId`; jméno a e-mail zůstávají jako kopie, formulář je předvyplní z účtu.
- `OrderService`: seznam a detail podle uživatele (admin vidí vše), cizí objednávka = 404 (stránky, API, Journeys).
- Založit objednávku smí jen role User (stránka i `POST /api/orders`).
- Změna schématu → nová `ordering.db` (lokálně i v cloudu; staré objednávky nemají vlastníka).

**Hotovo, když:** dva uživatelé nevidí objednávky toho druhého, admin vidí obě a nemůže objednat.

## Fáze B: EDA a e-maily

## Krok 4: Zrušení objednávky (i ve stavu Pending) – odloženo

*Odloženo 30. 10. 2026 – na dalších krocích nezávisí. Zrušit jde zatím jen potvrzenou objednávku.
Rozbor souběhu (dohnat zrušení v orchestrátoru před odpovědí skladu, `Status` objednávky jako concurrency
token, 409 při souběžném zápisu) je v `CLAUDE.md` a v tomto plánu níže.*

**Teorie.** Zrušení potvrzené objednávky už sága umí (uvolní rezervaci). Ve stavu Pending je ale příkaz
`ReserveStockV1` možná už ve skladu a odpověď je „na cestě“. Objednávku můžeme zrušit hned, ale sága musí počkat
na odpověď skladu: přijde-li rezervace, pošle kompenzaci `ReleaseStockV1`; přijde-li odmítnutí, není co vracet.
Stejný vzor už MiniShop má pro rezervaci, která dorazí po timeoutu.

**V MiniShopu:**

- `Order.Cancel` povolí Pending i Confirmed.
- `OrderSaga`: zrušení ve stavu `AwaitingReservation` → nový stav (čeká na odpověď skladu), pozdní `StockReservedV1`
  → `ReleaseStock`, `StockReservationFailedV1` nebo timeout → konec.
- Souběh „sága potvrzuje, uživatel ruší“ řeší verze ságy a objednávky (optimistická souběžnost).
- Tlačítko Zrušit pro vlastníka i admina.

**Hotovo, když:** testy pokrývají všechna pořadí (zrušení před/po rezervaci, před odmítnutím, před timeoutem)
a zásoba na skladě vždy sedí.

## Krok 5: Skutečné e-maily

**Teorie.** E-mail je vnější akce – odeslání nejde vrátit. Proto se posílá z handleru za outboxem (spolehlivě,
s opakováním) a lokálně do „falešné pošty“, aby vývoj nikomu nic neposlal.

**V MiniShopu:**

- `IEmailSender` + SMTP přes MailKit; lokálně kontejner **Mailpit** v Aspire (webové UI se zachycenými e-maily).
- Produkce zdarma: **samostatný Gmail účet** (`ftichy.gemini@gmail.com`) s heslem aplikace (cca 500 e-mailů/den, dobrá doručitelnost bez vlastní
  domény); alternativa Brevo (300/den). Limity ověřit před krokem.
- Potvrzení e-mailu po registraci, zapomenuté heslo, e-mail o stavu objednávky místo `LoggingOrderNotifier`.
- Secret `SMTP_PASSWORD` (`docs/secrets.md`, `deploy.yml`, `deploy.sh`), netajné nastavení do `production.env`.

**Hotovo, když:** lokálně je e-mail v Mailpitu, z cloudu dorazí do skutečné schránky.

## Fáze C: správa

## Krok 6: Administrace uživatelů

**V MiniShopu:**

- Seznam uživatelů (počet a součet objednávek), detail uživatele s jeho objednávkami.
- Povýšení na admina (jen existující uživatel), odebrání role, zablokování a odblokování (lockout bez konce).
- Poslední admin nejde odebrat ani zablokovat.
- Auditní záznam (kdo, komu, co, kdy) v `users.db`.

**Hotovo, když:** admin povýší uživatele, zablokovaný uživatel se nepřihlásí, posledního admina nejde odebrat.

## Krok 7: Zabezpečení skladu (Inventory.Api)

**Teorie.** Dnes je API skladu otevřené – v cloudu ho chrání jen to, že není publikované ven. **Zero trust**: každá
služba si ověří, kdo volá. Nejjednodušší je sdílený servisní klíč; správnější je předat token uživatele (JWT)
a nechat sklad ověřit roli sám.

**V MiniShopu:**

- Zapisovací endpointy skladu vyžadují hlavičku se servisním klíčem; `InventoryApiClient` ho posílá.
  Kdo smí co, rozhoduje Web (krok 2). Čtení zůstává bez klíče.
- Secret `INVENTORY_API_KEY` (oba kontejnery), lokálně ho předá Aspire.
- Volitelně: JWT od Webu, sklad ověří roli Admin. **Odloženo** (krok 7 zavedl jen servisní klíč, i pro čtení).

**Hotovo, když:** zápis bez klíče vrátí 401, aplikace funguje dál.

## Fáze D: provoz a otevření

## Krok 8: Zpevnění

- Lockout (5 pokusů / 15 minut), rate limiting na přihlášení, registraci a reset hesla.
- Bezpečnostní hlavičky (CSP, HSTS, X-Frame-Options) – nejspíš v Caddy.
- Do trasy jen `user.id`; kontrola, že logy neobsahují e-maily ani hesla; audit neúspěšných přihlášení.

**Hotovo, když:** opakované špatné heslo účet dočasně zamkne, příliš mnoho požadavků vrátí 429.

## Krok 9: Otevření webu

- `BASIC_AUTH=off` pro web; Aspire dashboard basic auth ponechá.
- Ochrana registrace: objednat jde až s potvrzeným e-mailem; volitelně CAPTCHA (Cloudflare Turnstile, zdarma).
- GDPR minimum: uživatel může smazat účet → anonymizace (objednávky zůstanou bez osobních údajů).

**Hotovo, když:** web je veřejný bez basic auth a registrace je chráněná.

## Krok 10: Přihlášení přes Google

**Teorie.** OpenID Connect: MiniShop přesměruje na Google, ten ověří uživatele a vrátí podepsanou identitu.
Heslo k Googlu MiniShop nikdy nevidí.

**V MiniShopu:**

- OAuth klient v Google Cloud Console (zdarma), redirect `https://minishop-tichy.duckdns.org/signin-google`.
- `AddGoogle`; párování na existující účet jen přes ověřený e-mail; účet bez hesla je v pořádku.
- Secrets `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`.

**Hotovo, když:** nový i existující uživatel se přihlásí Googlem, role zůstávají v MiniShopu.

## Volitelně: kam dál

- **Dvoufázové ověření (TOTP)** pro adminy – Identity ho umí (`AuthenticatorTokenProvider`).
- **Passkeys (WebAuthn)** – přihlášení bez hesla, v .NET 10 přímo v Identity.
- **EF Core migrace** – s účty už nepůjde při změně schématu smazat databázi.
- **Událost `UserRegisteredV1`** – kdyby účty potřebovala jiná služba (uvítací e-mail, CRM).

## Shrnutí nákladů

| Položka | Služba | Cena |
| --- | --- | --- |
| Účty, role | ASP.NET Core Identity | 0 Kč |
| E-maily | Gmail SMTP (samostatný účet) nebo Brevo free | 0 Kč |
| Lokální pošta | Mailpit (kontejner) | 0 Kč |
| CAPTCHA (volitelně) | Cloudflare Turnstile | 0 Kč |
| Přihlášení přes Google | Google OAuth | 0 Kč |
| **Celkem** | | **0 Kč** |

## Postup práce na každém kroku

Stejně jako u EDA a cloudu: nejdřív shrnutí (teorie + co se změní v MiniShopu) a souhlas, pak malé změny,
build + testy, úprava příručky `/Docs`, souhrn do `docs/kroky-ucty/krok-NN-nazev.md` (Jak to funguje, Nové soubory,
Změněné soubory, Rozhodnutí, Známá omezení, Jak ověřit, Vyzkoušejte sami) a aktualizace tabulky v `CLAUDE.md`.
Kroky mimo repozitář (Google Cloud Console, Gmail, GitHub secrets) dělá uživatel sám podle návodu.
