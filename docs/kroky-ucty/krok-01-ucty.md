# Účty krok 1: Účty, registrace, přihlášení

MiniShop dosud nevěděl, kdo s ním pracuje – jméno a e-mail se psaly ručně do každé objednávky. Teď vzniká modul
**Accounts**: uživatel se zaregistruje e-mailem a heslem, přihlásí se a v hlavičce vidí své jméno. Zatím se nic
nezamyká (objednávat jde i bez přihlášení) – role a pravidla „kdo smí co“ přijdou v kroku 2. Plán celé řady:
[docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: na recepci hotelu ukážete občanku a dostanete kartu od pokoje. Recepce = přihlášení (porovná heslo),
karta = **cookie** se zašifrovanou identitou. Dveře (každý další požadavek) čtou jen kartu – do knihy hostů
(databáze) se nedívají.

```
 Registrace:  formulář → UserManager.CreateAsync
                           ├─ pravidla hesla (≥ 10 znaků), unikátní e-mail
                           └─ users.db: uživatel + HASH hesla se solí (heslo samo nikde)
                         → SignInManager.SignInAsync → Set-Cookie .MiniShop.Auth (ID, e-mail, jméno)

 Přihlášení:  formulář → SignInManager.PasswordSignInAsync → porovná hash → cookie
                                                           → nebo „Neplatný e-mail nebo heslo.“

 Každý další požadavek:  cookie → UseAuthentication → HttpContext.User (bez dotazu do DB)
 Odhlášení:   POST /Account/Logout (s antiforgery tokenem) → cookie smazána
```

Kde je co:

- **Infrastructure/Accounts** – vše, co nepotřebuje web: `ApplicationUser`, `AccountsDbContext` (`users.db`),
  pravidla Identity, české hlášky, co jde do cookie. `AddAccountsInfrastructure` vrací `IdentityBuilder`.
- **Web** – `AddAccounts` k tomu přidá `SignInManager` a přihlašovací cookie; stránky `Pages/Account/*`,
  `_LoginPartial` v hlavičce, `UseAuthentication`/`UseAuthorization` v pipeline.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/ApplicationUser.cs` | Účet (dědí z `IdentityUser`) + `DisplayName` |
| `src/MiniShop.Infrastructure/Accounts/AccountsDbContext.cs` | `IdentityDbContext` nad `users.db` (i tabulky rolí pro krok 2) |
| `src/MiniShop.Infrastructure/Accounts/AccountsInfrastructure.cs` | `AddAccountsInfrastructure`: DbContext, jádro Identity, pravidla hesla |
| `src/MiniShop.Infrastructure/Accounts/AccountsDatabaseInitializer.cs` | `EnsureCreated` pro `users.db` |
| `src/MiniShop.Infrastructure/Accounts/AccountClaimsPrincipalFactory.cs` | Zobrazované jméno do cookie (`AccountClaimTypes.DisplayName`) |
| `src/MiniShop.Infrastructure/Accounts/CzechIdentityErrorDescriber.cs` | České chybové hlášky Identity |
| `src/MiniShop.Web/Extensions/AccountsServiceCollectionExtensions.cs` | `AddAccounts`: SignInManager, nastavení cookie, 401/403 pro `/api` |
| `src/MiniShop.Web/Pages/Account/Register.cshtml(.cs)` | Registrace, po úspěchu rovnou přihlášení |
| `src/MiniShop.Web/Pages/Account/Login.cshtml(.cs)` | Přihlášení, „Zapamatovat si mě (14 dní)“ |
| `src/MiniShop.Web/Pages/Account/Logout.cshtml(.cs)` | Odhlášení jen přes POST |
| `src/MiniShop.Web/Pages/Shared/_LoginPartial.cshtml` | Pravá část hlavičky: jméno + Odhlásit / Přihlásit + Registrovat |
| `tests/MiniShop.Tests/Infrastructure/AccountsTests.cs` | Hash hesla, pravidla hesla, duplicitní e-mail, ověření hesla, claims |
| `docs/prirucka/architektura/moduly/accounts.md` | Stránka modulu v příručce |
| `docs/Ucty-roadmap.md` | Plán 10 kroků účtů a rolí |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/MiniShop.Infrastructure.csproj` | Balíček `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13 |
| `src/MiniShop.Web/Program.cs` | Connection string `Accounts`, `AddAccounts`, inicializace `users.db`, `UseAuthentication`/`UseAuthorization` |
| `src/MiniShop.Web/appsettings.json` | `ConnectionStrings:Accounts = Data Source=users.db` |
| `src/MiniShop.Web/Dockerfile` | `ENV ConnectionStrings__Accounts="Data Source=/data/users.db"` |
| `src/MiniShop.Web/Pages/Shared/_Layout.cshtml` | `_LoginPartial` v hlavičce |
| `deploy/backup.py` | `users.db` v záloze; obnova starší zálohy bez `users.db` ho přeskočí (nechá současný) |
| `tests/MiniShop.Tests/Architecture/ModuleBoundariesTests.cs` | Modul Accounts: nesmí znát ostatní moduly a ony jeho |
| `docs/prirucka/…` | Moduly, kontejnery, lokální běh, zálohy, knihovny, slovníček, historie |
| `CLAUDE.md` | Tabulka „Stav uživatelských účtů“ |

## Rozhodnutí v tomto kroku

- **ASP.NET Core Identity, žádná vlastní kryptografie.** Hash PBKDF2 se solí, security stamp, později zámek účtu,
  reset hesla a Google – vše hotové a prověřené.
- **Účet není doménová entita.** Nemá business pravidla, je to „technika“ přihlašování → `Infrastructure/Accounts`.
  Ostatní moduly budou znát jen jeho ID (test hranic modulů to hlídá už teď).
- **Jádro Identity v Infrastructure, přihlašování ve Webu.** `AddIdentityCore` (UserManager, pravidla) nepotřebuje
  ASP.NET Core; cookie a `SignInManager` ano. Testy tak ověřují stejná pravidla jako aplikace, jen bez HTTP.
- **Vlastní české stránky místo hotového Identity UI** (to je anglické, má desítky stránek a jiný vzhled).
- **Heslo podle NIST:** ≥ 10 znaků, bez povinných velkých písmen, číslic a speciálních znaků.
- **Přihlašovací jméno = e-mail**, jeden e-mail = jeden účet (bez ohledu na velikost písmen).
- **Cookie** `.MiniShop.Auth`: `HttpOnly`, `Secure` (vždy – prohlížeče berou `http://localhost` jako bezpečný),
  `SameSite=Lax`, 14 dní s posuvnou platností. Šifrují ji klíče Data Protection – v cloudu na volume (cloud krok 1),
  takže přihlášení přežije nasazení.
- **`/api/*` vrací 401/403**, ne přesměrování na HTML přihlášení (připraveno pro krok 2).
- **Stejná hláška pro neznámý e-mail i špatné heslo**, návrat po přihlášení jen na lokální adresu (open redirect),
  odhlášení jen POST, do logu jen ID uživatele.
- **Tabulky rolí vznikají už teď** – krok 2 nebude muset mazat `users.db`.

## Známá omezení

- Nic se nezamyká – kdo není přihlášený, smí totéž co přihlášený (krok 2 a 3).
- Registrace prozradí, že e-mail už má účet („Účet s tímto e-mailem už existuje.“). Bez odesílání e-mailů to jinak
  nejde; řeší se s potvrzením e-mailu (krok 5).
- Bez potvrzení e-mailu, resetu hesla a změny hesla (krok 5).
- Bez zámku účtu po opakovaném špatném hesle a bez rate limitingu (krok 8).
- Objednávka zatím s účtem nijak nesouvisí – jméno a e-mail se píšou ručně (krok 3).
- `EnsureCreated` bez migrací: změna schématu `users.db` = smazat databázi (a s ní účty).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. Otevřete Web (https://localhost:7180), vpravo nahoře **Registrovat**.
2. Zkuste heslo kratší než 10 znaků → „Heslo musí mít alespoň 10 znaků.“, neshodná hesla → „Hesla se neshodují.“
3. Zaregistrujte se → jste přihlášení, v hlavičce je vaše jméno. Vedle projektu Webu vznikl `users.db`.
4. **Odhlásit** → **Přihlásit** se špatným heslem → „Neplatný e-mail nebo heslo.“ (stejně pro neexistující e-mail).
5. Znovu registrace se stejným e-mailem (i VELKÝMI písmeny) → „Účet s tímto e-mailem už existuje.“
6. V nástrojích prohlížeče (Application → Cookies) cookie `.MiniShop.Auth`: příznaky HttpOnly a Secure.

Cloud: po sloučení a nasazení vznikne `/data/users.db` ve volume `web-data`; další noční záloha ho obsahuje
(`sudo -u deploy /opt/minishop/restore.sh list`, v logu zálohy řádek `users.db: … kB`).

## Vyzkoušejte sami

- Otevřete `users.db` (např. DB Browser for SQLite) a podívejte se do `AspNetUsers` na sloupec `PasswordHash`.
  Zaregistrujte dva účty se stejným heslem – proč se hashe liší? (sůl)
- Smažte v prohlížeči cookie `.MiniShop.Auth` a obnovte stránku – kde zmizelo přihlášení a proč se server
  na nic neptal databáze?
- Zkuste `https://localhost:7180/Account/Login?returnUrl=https://example.com` a přihlaste se. Kam vás to pošle a proč?
- Zavolejte `GET /Account/Logout` přímo v adresním řádku. Proč vás to neodhlásí?
