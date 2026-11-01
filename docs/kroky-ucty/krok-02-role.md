# Účty krok 2: Role a první admin

Po kroku 1 MiniShop věděl, **kdo** je přihlášený, ale každý směl všechno. Teď přibývají dvě role a pravidla
**co kdo smí**: zákazník (User) objednává, správce (Admin) spravuje sklad, produkty a vyřizuje objednávky – sám
neobjednává. Nepřihlášený vidí jen úvod, dokumentaci a přihlášení. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: hotel, kde jsou **všechny dveře zamčené** a odemčená je jen recepce a lobby (fallback policy).
Karta hosta (cookie s rolí User) otevře pokoj a restauraci, karta personálu (Admin) sklad a kancelář – ale personál
si nemůže objednat pokojovou službu. První kartu personálu nevydá recepce, ale majitel osobně v serverovně
(příkaz `create-admin`).

```
 požadavek ─► UseAuthentication (cookie → User: ID, jméno, ROLE)
          ─► UseMustChangePassword (dočasné heslo → jen /Account/ChangePassword)
          ─► UseAuthorization:
               [AllowAnonymous]                → pustit           (úvod, Docs, přihlášení, /health)
               [Authorize(Policy = RequireAdmin)] → jen role Admin (Produkty, Průběh, Přehrání, API vyřízení…)
               [Authorize(Policy = RequireCustomer)] → jen role User (POST /api/orders)
               nic                             → fallback: přihlášený (Objednávky, změna hesla, ostatní API)
          ─► stránka Objednávky: handler sám ověří policy (IAuthorizationService) → jinak Forbid()

 bez oprávnění:  nepřihlášený → stránka 302 /Account/Login,       API 401
                 přihlášený   → stránka 302 /Account/AccessDenied, API 403
```

**První admin:**

```
 produkce:  docker compose exec web dotnet MiniShop.Web.dll create-admin --email … → heslo do terminálu (skrytě)
                └─ AccountsSeeder.CreateFirstAdminAsync: jen když žádný admin není a e-mail není obsazený
 vývoj:     start Webu → AccountsSeeder.SeedAsync(AdminBootstrap z appsettings.Development.json)
                └─ admin@minishop.local + claim „musí změnit heslo“ → po přihlášení jen změna hesla
 vždy:      role User a Admin existují, účty bez role (z kroku 1) dostanou User
```

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountRoles.cs` | Názvy rolí `User`, `Admin` |
| `src/MiniShop.Infrastructure/Accounts/AccountsSeeder.cs` | Role, role User pro účty bez role, první admin (`AdminBootstrapSettings`, `CreateFirstAdminAsync`) |
| `src/MiniShop.Web/Accounts/AuthorizationPolicies.cs` | Názvy policies `RequireAdmin`, `RequireCustomer` |
| `src/MiniShop.Web/Accounts/MustChangePasswordMiddleware.cs` | S dočasným heslem jen na změnu hesla (API 403) |
| `src/MiniShop.Web/Accounts/CreateAdminCommand.cs` | Příkaz `create-admin` – první admin bez spuštění webu, heslo skrytě z terminálu |
| `src/MiniShop.Web/Pages/Account/ChangePassword.cshtml(.cs)` | Změna hesla (pro všechny), odebere příznak „musí změnit heslo“ |
| `src/MiniShop.Web/Pages/Account/AccessDenied.cshtml` | „Přístup odepřen“ |
| `tests/MiniShop.Tests/Web/MiniShopWebFactory.cs` | Celý Web v paměti (`WebApplicationFactory`), přihlášení přes skutečný formulář |
| `tests/MiniShop.Tests/Web/AuthorizationMatrixTests.cs` | Matice oprávnění: nepřihlášený/User/Admin × stránky, akce, API (48 případů) |
| `tests/MiniShop.Tests/Web/MustChangePasswordTests.cs` | Dočasné heslo → vynucená změna → pak vše funguje |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountClaimsPrincipalFactory.cs` | Claim `AccountClaimTypes.MustChangePassword` |
| `src/MiniShop.Infrastructure/Accounts/AccountsDatabaseInitializer.cs` | Po `EnsureCreated` spustí seed (role, první admin z konfigurace) |
| `src/MiniShop.Infrastructure/Accounts/AccountsInfrastructure.cs`, `CzechIdentityErrorDescriber.cs` | Registrace seederu; hláška „Současné heslo není správné.“ |
| `src/MiniShop.Web/Extensions/AccountsServiceCollectionExtensions.cs` | Policies, fallback policy, `AccessDeniedPath` |
| `src/MiniShop.Web/Program.cs` | Příkaz `create-admin`, seed s `AdminBootstrap`, `UseMustChangePassword` |
| `src/MiniShop.Web/appsettings.Development.json` | `AdminBootstrap` (vývojový admin `admin@minishop.local`) |
| `src/MiniShop.Web/Pages/Orders/Index.cshtml(.cs)` | Kontrola policy v handlerech; formulář jen zákazník; Vyřídit, Obnovit katalog, Průběh jen admin |
| `src/MiniShop.Web/Pages/Products/Index.cshtml.cs`, `Orders/Journey`, `Orders/Replay` | `[Authorize(Policy = RequireAdmin)]` |
| `src/MiniShop.Web/Pages/Index.cshtml`, `Docs`, `Account/Login`, `Register`, `Logout` | `[AllowAnonymous]` |
| `src/MiniShop.Web/Pages/Account/Register.cshtml.cs` | Nový účet dostane roli User |
| `src/MiniShop.Web/Pages/Shared/_Layout.cshtml`, `_LoginPartial.cshtml` | Menu Produkty jen admin; jméno → změna hesla, štítek „admin“ |
| `src/MiniShop.Web/Controllers/OrdersController.cs`, `JourneysController.cs` | Policies na založení, vyřízení a průběh |
| `src/MiniShop.Web/MiniShop.Web.http` | Cookie pro volání API |
| `src/MiniShop.ServiceDefaults/ServiceDefaultsExtensions.cs` | `/health` a `/alive` s `AllowAnonymous` |
| `tests/MiniShop.Tests/MiniShop.Tests.csproj` | Balíček `Microsoft.AspNetCore.Mvc.Testing` |
| `tests/MiniShop.Tests/Infrastructure/AccountsTests.cs` | Testy seedu a prvního admina |
| `docs/…` | Příručka (Accounts, knihovny, slovníček, historie), roadmapa (varianta B), `CLAUDE.md` |

## Rozhodnutí v tomto kroku

- **Fallback policy „musí být přihlášen“ (secure by default).** Veřejné stránky jsou vyjmenované (`[AllowAnonymous]`),
  ne zamčené. Zapomenutá stránka je zamčená, ne otevřená.
- **Policies místo `User.IsInRole(...)` roztroušených po kódu** – pravidlo se mění na jednom místě.
- **Stránka Objednávky kontroluje oprávnění v handlerech** (`IAuthorizationService`) – Razor Pages nemají
  `[Authorize]` na jednotlivý handler. Stejné policies, žádná duplicitní pravidla.
- **Admin neobjednává** (oddělení povinností) – formulář nevidí a server mu `Create` odmítne.
- **První admin v produkci příkazem `create-admin` (varianta B), ne secretem.** Heslo nikdy neopustí terminál,
  `deploy.sh` ani GitHub se nemění. Admin vzniká jednou za život aplikace; na nové VM se vrátí s `users.db` ze zálohy.
- **Seed nikdy nepovýší existující účet** – bez potvrzení e-mailu by si adminův e-mail mohl zaregistrovat kdokoli.
  Příkaz i seed projdou jen tehdy, když žádný admin není (dalšího jmenuje admin v aplikaci – krok 6).
- **Příznak „musí změnit heslo“ jako claim uživatele** – bez nového sloupce, `users.db` se nemaže.
- **Matice oprávnění testovaná přes skutečné HTTP** (`WebApplicationFactory`) včetně cookie, antiforgery a policies.

## Známá omezení

- User zatím vidí **všechny** objednávky a může zrušit jakoukoli (vlastník objednávky – krok 3, zrušení – krok 4).
- Jméno a e-mail v objednávce se stále píšou ručně (krok 3).
- Změna role se v cookie projeví až po novém přihlášení nebo do 30 minut (security stamp). Zatím role nikdo
  v aplikaci nemění (krok 6).
- Druhého admina zatím nejde vytvořit (krok 6) – `create-admin` funguje jen bez admina.
- Zapomenuté heslo admina: zatím jen smazání účtu v `users.db` a nový `create-admin` (reset hesla e-mailem – krok 5).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. Nepřihlášený: úvod a Dokumentace jdou, **Objednávky** → přihlašovací stránka.
2. Přihlaste se jako `admin@minishop.local` / `admin-heslo-pro-vyvoj` → hned **Změna hesla** (jinam to nepustí).
   Po změně vidíte menu **Produkty**, u objednávek **Vyřídit** a **Průběh zadání**, ale ne formulář nové objednávky.
3. Odhlaste se, zaregistrujte zákazníka → formulář objednávky je, menu Produkty ne, `/Products` → „Přístup odepřen“.
4. Účet z kroku 1 (pokud ho máte v `users.db`) dostal roli User – v logu `Uživatel … bez role dostal roli User`.

### Produkce – první admin (jednou, po nasazení)

Na VM (přes Tailscale):

```text
ssh minishop-vm
cd /opt/minishop
sudo -u deploy docker compose exec web dotnet MiniShop.Web.dll create-admin --email <váš-admin-e-mail> --name "<jméno>"
```

Zadejte dvakrát heslo (nezobrazuje se). Pak se na webu přihlaste tímto e-mailem – vedle jména je štítek **admin**.
Pro admina doporučujeme **jiný e-mail než zákaznický účet** (jeden e-mail = jeden účet a admin neobjednává).

## Vyzkoušejte sami

- Odeberte `[AllowAnonymous]` z `Pages/Docs/Index.cshtml.cs` a spusťte testy. Který spadne a proč?
- Smažte `[Authorize(Policy = ...)]` u `Complete` v `OrdersController` – test matice to odhalí. Pak vraťte.
- Jako zákazník pošlete v nástrojích prohlížeče (F12 → Network → „Edit and resend“) POST na
  `/Orders?handler=Complete&id=1`. Tlačítko nevidíte, ale co udělá server?
- Podívejte se do `users.db` na tabulky `AspNetRoles`, `AspNetUserRoles` a `AspNetUserClaims`.
