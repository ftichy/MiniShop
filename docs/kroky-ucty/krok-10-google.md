# Účty krok 10: Přihlášení přes Google

Tlačítko **Přihlásit přes Google** na přihlášení i registraci. Nový zákazník tak nemusí vymýšlet heslo ani potvrzovat
e-mail (ověřil ho Google), stávající si Google propojí. Role a objednávky zůstávají v MiniShopu.
Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Protokol **OpenID Connect** (nad OAuth 2.0). Přirovnání: vrátný (MiniShop) tě nezná, ale uzná občanku (Google). Pošle
tě na úřad, úřad tě ověří a dá ti potvrzení s razítkem, vrátný zkontroluje razítko. Heslo ke Googlu MiniShop nevidí.

1. `POST /Account/ExternalLogin` (provider `Google`) → `Challenge` → 302 na `accounts.google.com` s `client_id`
   a `state` (+ korelační cookie).
2. Google uživatele přihlásí a vrátí na `/signin-google?code=…&state=…`. Handler Googlu (ASP.NET Core) ověří `state`,
   kód u Googlu vymění (s client secret) za identitu – `sub` (stálé ID), e-mail, `email_verified`, jméno – a uloží ji
   do dočasné cookie Identity (5 minut).
3. `ExternalLogin?handler=Callback`: `ExternalLoginSignInAsync` – propojený účet = přihlásit (zámek a blokace jako
   u hesla). Jinak `ExternalAccountLinker.LinkOrCreateAsync`:

| Situace | Výsledek |
| --- | --- |
| účet se stejným e-mailem, e-mail potvrzený | propojit (`AspNetUserLogins`) a přihlásit |
| účet se stejným e-mailem, e-mail nepotvrzený | odmítnout („přihlaste se heslem a potvrďte e-mail“) |
| žádný účet | založit bez hesla, role User, `EmailConfirmed = true` |
| Google e-mail neověřil | odmítnout |
| zrušení na Googlu / neplatný návrat | `OnRemoteFailure` → stránka s hláškou (jinak 500) |

Další cesty:

- **Nastavení účtu → Přihlášení přes Google** (`/Account/ExternalLogins`): propojit (i nepotvrzený účet – uživatel je
  přihlášený heslem, ID účtu jde ve `state`) a odpojit (jen s heslem nebo jiným propojením).
- **Nastavit heslo** (`/Account/SetPassword`): účet bez hesla; *Změna hesla* sem bez hesla přesměruje.
- **Smazání účtu bez hesla:** tlačítko *Ověřit přes Google* → Google s `prompt=login` (zeptá se znovu) → návrat
  `Delete?handler=GoogleConfirmed` → smaže se, jen když se vrátil propojený účet Google. Propojení zmizí s účtem (kaskáda).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/ExternalAccountLinker.cs` | pravidla propojení / založení / odmítnutí, propojení z nastavení, odpojení |
| `src/MiniShop.Web/Accounts/GoogleLogin.cs` | `AddGoogle` jen s nastaveným klientem, `email_verified` → claim, `OnRemoteFailure`, `prompt=login` |
| `src/MiniShop.Web/Pages/Account/ExternalLogin.cshtml(.cs)` | start přihlášení a návrat z Googlu (`[AllowAnonymous]`, rate limiting) |
| `src/MiniShop.Web/Pages/Account/ExternalLogins.cshtml(.cs)` | propojit / odpojit z nastavení účtu |
| `src/MiniShop.Web/Pages/Account/SetPassword.cshtml(.cs)` | nastavení hesla pro účet bez hesla |
| `src/MiniShop.Web/Pages/Account/_AccountFooter.cshtml` | společné odkazy pod změnou/nastavením hesla |
| `src/MiniShop.Web/Pages/Shared/_ExternalLoginButtons.cshtml` | tlačítko na přihlášení a registraci (bez klienta nic) |
| `tests/MiniShop.Tests/TestDoubles/FakeGoogle.cs` | falešné schéma „Google“ – identita z hlaviček testu, bez internetu |
| `tests/MiniShop.Tests/Web/GoogleLoginTests.cs` | 12 testů celé cesty přes stránky |

## Změněné soubory

- `MiniShop.Web.csproj` – balíček `Microsoft.AspNetCore.Authentication.Google`, `UserSecretsId` (lokální klient).
- `AccountsServiceCollectionExtensions` / `Program.cs` – `AddGoogleLogin(configuration)`.
- `AccountsInfrastructure` – registrace `ExternalAccountLinker`.
- `SecurityHeaders` – `form-action 'self' https://accounts.google.com`.
- `Login`, `Register` – tlačítko; `ChangePassword` – bez hesla → `SetPassword`, patička v partialu.
- `Delete` – potvrzení přes Google pro účet bez hesla.
- `Admin/Users/Detail` – řádek *Přihlášení* (heslo, Google).
- `Privacy` – ID účtu Google, Google jako zpracovatel, dočasná cookie.
- Nasazení: `compose.yaml` (`Authentication__Google__*`), `production.env` (`GOOGLE_CLIENT_ID`), `.env.example`,
  `deploy.yml` + `deploy.sh` (secret `GOOGLE_CLIENT_SECRET`, tvar `GOCSPX-…`), `docs/secrets.md` (řádek + postup R12).
- Testy: `MiniShopWebFactory` (falešný Google), `AuthorizationMatrixTests` (nové stránky a akce).
- Příručka: Accounts (sekce Přihlášení přes Google), knihovny, služby, secrets, lokální vývoj, slovníček, historie.

## Rozhodnutí v tomto kroku

- **Nepotvrzený e-mail se automaticky nepropojí** (*pre-account hijacking* – útočník by předem založil účet s cizím
  e-mailem a svým heslem). Přihlášený uživatel si propojit může vždy.
- **Účet z Googlu nemá heslo** – žádné vymyšlené náhodné heslo; heslo si může nastavit sám.
- **Smazání bez hesla = nové přihlášení u Googlu** (`prompt=login`), ne opsání e-mailu – to by nechránilo před někým
  u odemčeného prohlížeče.
- **Admin smí přes Google** se stejnými pravidly; role určuje MiniShop.
- **Žádná migrace:** `AspNetUserLogins` existuje od kroku 1.
- **Google volitelný:** bez Client ID a tajemství tlačítko zmizí (jako e-maily bez `Email:Host`); Client ID není tajné
  (v `production.env`), tajemství je secret.
- **`isPersistent: false`** – jako přihlášení heslem bez „Zapamatovat si mě“.

## Známá omezení

- Jméno a e-mail se z Googlu převezmou jen při založení účtu – pozdější změna u Googlu se do MiniShopu nepromítne.
- Účet bez hesla bez propojení (nepovedené propojení po založení) se přihlásí jen přes *Zapomenuté heslo* nebo
  znovu přes Google (propojí se podle potvrzeného e-mailu).
- Souběžné první přihlášení stejným účtem Google ze dvou oken může skončit chybou u druhého (unikátní e-mail) –
  stačí to zkusit znovu.
- Do auditu (`AccountAuditEntries`) se propojení nezapisuje – audit je pro akce admina; propojení je v logu (ID účtu).

## Jak ověřit

- `dotnet test MiniShop.slnx` – `GoogleLoginTests`, `AuthorizationMatrixTests`, `HardeningTests`, `DocumentationTests`.
- Lokálně se skutečným Googlem: OAuth klient s návratem `https://localhost:7180/signin-google`, user secrets
  (viz příručka *Lokální vývoj*), `dotnet run --project src/MiniShop.AppHost` → *Přihlásit přes Google*.

### Produkce

1. Google Cloud Console: projekt, consent screen (External, *Publish app*), OAuth klient s návratem
   `https://minishop-tichy.cz/signin-google`.
2. `GOOGLE_CLIENT_ID` do `deploy/production.env`, secret `GOOGLE_CLIENT_SECRET` do environmentu `production`.
3. Nový `deploy.sh` na VM **před sloučením** (starý by neznámý klíč `GOOGLE_CLIENT_SECRET` odmítl).
4. Nový účet přes Google, propojení existujícího účtu, nastavení hesla, smazání účtu bez hesla.

**Provedeno 6. 11. 2026:** projekt MiniShop v Google Cloud (consent screen External, rozsahy `openid` + e-mail + profil,
Branding s doménou `minishop-tichy.cz` a odkazem na `/Privacy`, aplikace publikovaná), klient s návraty pro produkci,
Aspire i Compose. Lokálně ověřeno se skutečným Googlem. Tajemství, které se při nastavování dostalo mimo správce hesel,
bylo hned vyměněno (*Add secret* → nové do user secrets a GitHubu → staré *Disable* + *Delete*). PR #69 nasazen
postupně (nový `deploy.sh` na VM předem), v produkci funguje přihlášení přes Google, účet bez hesla i smazání.

## Vyzkoušejte sami

1. Odhlaste se → *Přihlásit přes Google* → nový účet; v hlavičce jméno z Googlu, rovnou jde objednávat (e-mail potvrzený).
2. Jméno → *Přihlášení přes Google*: „propojeno“, *Odpojit* nejde – nejdřív *Nastavit heslo*.
3. Zaregistrujte účet heslem s e-mailem svého Googlu, e-mail **nepotvrzujte** → *Přihlásit přes Google* odmítne.
   Potvrďte e-mail → podruhé se účty propojí.
4. Účet bez hesla → *Smazat účet* → *Ověřit přes Google* → Google se zeptá znovu → účet je pryč.
