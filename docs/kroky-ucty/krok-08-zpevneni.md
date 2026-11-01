# Účty krok 8: Zpevnění

Do kroku 7 šlo heslo hádat donekonečna a prohlížeč nedostával žádná bezpečnostní pravidla. Teď přibyly čtyři
vrstvy obrany: dočasný zámek účtu, rate limiting formulářů účtu, bezpečnostní hlavičky (CSP, HSTS…) a kontrola,
že v logu nejsou osobní údaje. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: rate limiting je vrátný (ke dveřím pustí jen pár lidí za minutu), zámek účtu je platební karta
(po pár špatných PINech na chvíli nefunguje), CSP je provozní řád pro prohlížeč (nespustí nic, co nepřišlo z MiniShopu).

```
 POST /Account/Login ─► UseRateLimiter (policy account-forms: POST, podle IP, 10 / 60 s, jinak 429 + Retry-After)
                      ─► PasswordSignInAsync(lockoutOnFailure: true)
                           5 špatných hesel za sebou → LockoutEnd = teď + 15 min (úspěch počítadlo vynuluje)
                           výsledek: vždy stejná hláška; log „Neúspěšné přihlášení k účtu {UserId} (N. pokus)“

 každá odpověď ─► SecurityHeaders (OnStarting): CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy,
                  Permissions-Policy, Cross-Origin-Opener-Policy; mimo vývoj HSTS (1 rok)
 přihlášený ─► UseEndUserTracing: Activity tag enduser.id = ID účtu

 LockoutEnd:  rok 9999 = zablokovaný adminem (IsBlocked) │ do 15 min = dočasný zámek (LockedOutUntil)
```

**CSP** (`Content-Security-Policy`): `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`.
Ve vývoji navíc `connect-src ws://localhost:* wss://localhost:*` (obnovení prohlížeče z Visual Studia).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/Security/AccountRateLimiting.cs` | Policy `account-forms` (POST, podle IP), česká stránka 429 s `Retry-After` |
| `src/MiniShop.Web/Security/SecurityHeaders.cs` | Bezpečnostní hlavičky a `enduser.id` do trasy |
| `src/MiniShop.Web/wwwroot/js/orders-reload.js` | Automatické obnovení stránky Objednávky (dřív inline skript) |
| `src/MiniShop.Web/wwwroot/lib/bootstrap/` | Bootstrap 5.3.3 CSS lokálně (otisk ověřen proti oficiálnímu SRI) + licence |
| `tests/MiniShop.Tests/Web/HardeningTests.cs` | Zámek po 5 heslech, vynulování počítadla, 429, hlavičky, HSTS, žádné inline skripty, logy bez e-mailu a hesla |
| `tests/MiniShop.Tests/TestDoubles/RecordingLoggerProvider.cs` | Log do paměti testu |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountsInfrastructure.cs` | Zámek: 5 pokusů / 15 minut |
| `src/MiniShop.Web/Pages/Account/Login.cshtml.cs` | `lockoutOnFailure: true`, audit do logu s ID účtu, hláška s informací o zámku |
| `src/MiniShop.Web/Pages/Account/*` (6 stránek) | `[EnableRateLimiting]` |
| `src/MiniShop.Web/Program.cs` | `AddAccountRateLimiting`, `AddHsts`/`UseHsts`, `UseSecurityHeaders`, `UseEndUserTracing`, `UseRateLimiter` |
| `src/MiniShop.Infrastructure/Accounts/AccountAdministration.cs`, `Pages/Admin/Users/*` | Rozlišení zablokovaný / dočasně zamčený, „Zrušit dočasný zámek“; dočasný zámek admina práva nebere |
| `src/MiniShop.Web/Pages/Shared/_Layout.cshtml`, `Pages/Orders/Index.cshtml` | Bootstrap lokálně; inline skripty → `data-reload-when-idle` |
| `tests/…/MiniShopWebFactory.cs`, `AccountAdministrationTests.cs` | Vysoký limit pro testy, schránka logů; testy dočasného zámku |
| `docs/prirucka/…`, `CLAUDE.md` | Accounts (Zpevnění), knihovny (Bootstrap lokálně), historie |

## Rozhodnutí v tomto kroku

- **Hlavičky nastavuje aplikace, ne Caddy** – fungují i lokálně, hlídají je testy, Caddyfile zůstává jednoduchý.
- **CSP přísná pro skripty, volnější pro styly** – skripty jsou nejnebezpečnější (XSS); inline styly potřebuje Mermaid.
- **Bootstrap lokálně** – CSP bez cizích domén a jsDelivr nevidí návštěvníky (ani `Referer` stránky s tokenem).
- **HSTS bez `preload` a bez subdomén** – doména `duckdns.org` není naše, preload by šel špatně vrátit.
- **Zámek krátký (15 minut)** – jde zneužít k zamknutí cizího účtu; proti hromadnému zkoušení je rate limiting.
- **Stejná hláška i pro zamčený účet** – nic neprozradí; hláška ale vždy říká, že po 5 pokusech se účet zamkne.
- **Dočasný zámek admina práva nebere** (`IsBlocked` = jen zámek do roku 9999) – jinak by útočník zkoušením hesel
  „odstavil“ všechny adminy a pravidlo „vždy aktivní admin“ by nefungovalo.
- **Rate limit jen na POST a podle IP** – zobrazení stránky nic nestojí; v paměti procesu (jedna instance Webu).
- **Logy:** do logu ID účtu, nikdy e-mail ani heslo. Serilog loguje cestu bez query stringu; OpenTelemetry (1.20)
  hodnoty v query stringu maskuje sám.

## Známá omezení

- Útočník může cizí účet opakovaně zamykat (vždy na 15 minut) – plné řešení by byla CAPTCHA (krok 9, volitelně).
- Rate limit podle IP: lidé za společnou IP (firma, mobilní operátor) sdílí limit; útočník s mnoha IP ho obejde.
- Počítadla limitu jsou v paměti – restart je vynuluje; s více instancemi Webu by každá počítala zvlášť.
- `style-src 'unsafe-inline'` – vložený styl by se použil (nespouští ale kód).
- Do kroku 9 je před aplikací basic auth v Caddy, takže zvenku zámek ani limit zatím nikdo „nepotká“.
- Testy: Serilog používá statický logger – test logů běží na vlastní instanci Webu.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. **Prohlížeč, F12 → Console:** projděte Úvod, Objednávky (založte objednávku – stránka se sama obnoví),
   Průběh a Přehrání objednávky (admin), Dokumentaci (diagramy), Produkty. Žádná chyba „Content Security Policy“.
2. **F12 → Network → dokument → Response Headers:** `content-security-policy`, `x-frame-options: DENY`…
3. **Zámek:** v anonymním okně 5× špatné heslo k testovacímu účtu → pak ani správné neprojde; admin v *Uživatelé*
   vidí „zamčený do …“ a může zámek zrušit.
4. **Rate limit:** 11× rychle za sebou odeslat přihlášení → „Příliš mnoho pokusů“ (429).

### Produkce

Nic ručního – žádný nový secret ani změna `deploy.sh`. Po nasazení: na webu F12 → hlavičky a Console bez chyb CSP.
(Za Caddy funguje HSTS i IP klienta pro limit díky `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.)

## Vyzkoušejte sami

- Vraťte do `Orders/Index.cshtml` inline `<script>alert(1)</script>`. Co napíše konzole prohlížeče?
- https://securityheaders.com – zadejte adresu MiniShopu (po vypnutí basic auth v kroku 9). Jakou dostane známku?
- Proč je rate limit podle IP a ne podle e-mailu? (Nápověda: credential stuffing zkouší jedno heslo na tisíce e-mailů.)
