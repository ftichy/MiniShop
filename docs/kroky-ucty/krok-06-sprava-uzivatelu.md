# Účty krok 6: Správa uživatelů

Admin teď vidí všechny účty (s počtem objednávek a útratou), může uživatele povýšit na admina, vrátit na usera,
zablokovat a odblokovat. Systém hlídá, aby vždy zůstal aktivní admin, a každou změnu zapíše do auditu.
`users.db` kvůli tomu poprvé dostala novou tabulku – a s ní **EF Core migrace**. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: security stamp je zámek ve dveřích. Když admin uživateli změní roli nebo ho zablokuje, zámek se
přenastaví – starý klíč (cookie) do minuty přestane pasovat a dotyčný se musí přihlásit znovu (zablokovaný už nemůže).

```
 /Admin/Users (přehled)          AccountAdministration.GetAccountsAsync  (users.db)
                                 + IOrderService.GetCustomerSummariesAsync (ordering.db) – spojí Web přes ID
 /Admin/Users/Detail/{id}        objednávky přes OrderAccess.Customer(id), historie z AccountAuditEntries

 tlačítko (POST) ─► AccountAdministration.ChangeAsync
                      BEGIN TRANSACTION
                      volající = aktivní admin? (databáze, ne cookie)
                      zůstane jiný aktivní admin?   ne ─► chyba
                      vlastní účet (vrátit/zablokovat)? ─► chyba
                      role User ↔ Admin / zámek do DateTimeOffset.MaxValue / odemknutí
                      UpdateSecurityStamp ─► audit (kdo, komu, co, kdy)
                      COMMIT
 každý požadavek ─► SecurityStampValidator (1× za minutu): stamp v cookie ≠ databáze ─► odhlásit
```

**Migrace:** `AccountsDatabaseInitializer.MigrateAsync` – databáze bez `__EFMigrationsHistory`, ale s tabulkou
`AspNetUsers` (vznikla v krocích 1–5 přes `EnsureCreated`), dostane první migraci `InitialAccounts` zapsanou jako
provedenou; `Migrate` pak doplní jen `AccountAudit`. Nová databáze vznikne celá z migrací.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountAdministration.cs` | Správa uživatelů: přehled, detail, audit, povýšit / vrátit / zablokovat / odblokovat + pravidla |
| `src/MiniShop.Infrastructure/Accounts/AccountAuditEntry.cs` | Auditní záznam a `AccountAuditAction` |
| `src/MiniShop.Infrastructure/Accounts/AccountsDesignTimeDbContextFactory.cs` | Jen pro `dotnet ef` (vytvoření migrace bez spuštění Webu) |
| `src/MiniShop.Infrastructure/Accounts/Migrations/` | `InitialAccounts` (schéma kroků 1–5), `AccountAudit` (nová tabulka), snapshot |
| `dotnet-tools.json` | Lokální nástroj `dotnet-ef` 10.0.13 (`dotnet tool restore`) |
| `src/MiniShop.Web/Pages/Admin/Users/Index`, `Detail`, `_RoleBadge`, `_LockBadge` | Stránky správy uživatelů |
| `tests/MiniShop.Tests/Infrastructure/AccountAdministrationTests.cs` | Pravidla, audit, security stamp, migrace staré `users.db` |
| `tests/MiniShop.Tests/Web/UserAdministrationTests.cs` | Přes HTTP: přehled s objednávkou, povýšení, zablokovaný se nepřihlásí, vlastní detail bez tlačítek |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountsDbContext.cs` | Tabulka `AccountAuditEntries` |
| `src/MiniShop.Infrastructure/Accounts/AccountsDatabaseInitializer.cs` | `MigrateAsync` místo `EnsureCreated` + zápis výchozí migrace do staré databáze |
| `src/MiniShop.Infrastructure/Accounts/AccountsInfrastructure.cs` | Registrace `AccountAdministration` |
| `src/MiniShop.Infrastructure/MiniShop.Infrastructure.csproj` | `Microsoft.EntityFrameworkCore.Design` (jen pro vývoj, `PrivateAssets=all`) |
| `src/MiniShop.Application/Ordering/IOrderService.cs`, `OrderService.cs`, `IOrderRepository.cs`, `OrderContracts.cs`, `Infrastructure/Ordering/OrderRepository.cs` | `GetCustomerSummariesAsync` – počet objednávek a útrata za zákazníka |
| `src/MiniShop.Web/Extensions/AccountsServiceCollectionExtensions.cs` | `SecurityStampValidatorOptions.ValidationInterval` = 1 minuta |
| `src/MiniShop.Web/Program.cs` | `AuthorizeFolder("/Admin", RequireAdmin)` |
| `src/MiniShop.Web/Pages/Shared/_Layout.cshtml` | Menu **Uživatelé** (jen admin) |
| `src/MiniShop.Web/Pages/Account/Login.cshtml.cs` | Zablokovaný účet: stejná hláška, jiný záznam v logu |
| `src/MiniShop.Web/Accounts/CreateAdminCommand.cs` | Migrace místo `EnsureCreated` |
| `src/MiniShop.Infrastructure/Email/SmtpEmailSender.cs`, `tests/…/OrderOwnershipTests.cs` | Oprava dvou varování nullable |
| `tests/…/AuthorizationMatrixTests.cs`, `AccountsTests.cs` | Řádky pro `/Admin/Users`; test účtů přes migrace |
| `docs/prirucka/…` | Accounts (správa uživatelů, migrace), knihovny (EF Core Design), historie; `CLAUDE.md` |

## Rozhodnutí v tomto kroku

- **Migrace jen pro `users.db`** (varianta A) – účty se nesmí mazat a další kroky (smazání účtu, Google) schéma
  ještě změní. Ostatní moduly zůstávají na `EnsureCreated`.
- **Výchozí migrace = dnešní schéma**, stará databáze ji dostane jako provedenou – bez ručního SQL a bez ztráty dat.
- **Povýšit jde jen existující účet** – nový admin se zaregistruje sám, heslo nikdo nepřeposílá.
- **Role jsou výhradní** (User ↔ Admin) – admin neobjednává.
- **Rozhoduje databáze, ne cookie** – volající musí být aktivní admin i podle `users.db`.
- **Poslední admin a vlastní účet** – kontrola i změna v jedné transakci (souběžnou druhou změnu SQLite nepustí).
- **Blokace = zámek Identity do `DateTimeOffset.MaxValue`** – nepotřebuje nový sloupec; zámek po špatných heslech
  (krok 8) použije stejný mechanismus s krátkou dobou.
- **Security stamp při každé změně + kontrola každou minutu** (výchozí je 30 minut) – cena: jeden dotaz do `users.db`
  za minutu na přihlášeného uživatele.
- **Zablokovaný vidí „Neplatný e-mail nebo heslo.“** – jiná hláška by prozradila existenci účtu.
- **Útrata v paměti** – SQLite neumí `SUM` nad `decimal`; načtou se jen potřebné sloupce.
- **Bez potvrzovacích dialogů v JavaScriptu** – inline `onclick` by blokovala CSP (krok 8); akce jdou vrátit.

## Známá omezení

- Přehled uživatelů není stránkovaný ani nemá hledání (pro desítky účtů stačí).
- Útrata se počítá z celé tabulky objednávek při každém otevření přehledu.
- Nově povýšený admin musí se znovu přihlásit (je odhlášen do minuty).
- Audit se nečistí a neexportuje; obsahuje e-maily (osobní údaje) – řeší krok 9 (smazání účtu).
- Souběžná změna dvou adminů skončí u druhého chybou (500), ne hezkou hláškou – v praxi téměř nenastane.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V logu Webu při prvním startu: `Applying migration '…_AccountAudit'` (tvoje `users.db` zůstane, jen přibude tabulka).
2. Zaregistruj nový účet (např. `petra@test.local`) a vytvoř objednávku.
3. Odhlas se, přihlas jako `admin@minishop.local` → menu **Uživatelé** → Petra s 1 objednávkou.
4. Detail Petry → **Povýšit na admina** → zpráva + řádek v *Historie změn*. Petra se po novém přihlášení dostane na Produkty.
5. Na svém vlastním detailu tlačítka nejsou („To jste vy“).
6. **Zablokovat** Petru → v jiném prohlížeči (anonymní okno) se Petra nepřihlásí; byla-li přihlášená, do minuty ji to odhlásí.

### Produkce

Nic ručního – žádný nový secret ani změna `deploy.sh`. Po nasazení Web při startu migruje produkční `users.db`
(záloha je noční; před sloučením lze spustit ručně `sudo systemctl start minishop-backup`). Ověření: přihlásit se jako
admin → **Uživatelé**.

## Vyzkoušejte sami

- Otevřete `users.db` (např. DB Browser for SQLite) a podívejte se na `__EFMigrationsHistory` a `AccountAuditEntries`.
- Povyšte druhého admina, přihlaste se jím v anonymním okně a zkuste z něj vrátit původního admina. Pak zkuste
  z původního (stará cookie!) cokoli změnit.
- V `AccountsServiceCollectionExtensions` vraťte interval na 30 minut a zablokujte přihlášeného uživatele. Jak dlouho
  ještě může nakupovat?
- Přidejte do `ApplicationUser` vlastnost, vytvořte migraci (`dotnet ef migrations add …`) a prohlédněte si vygenerovaný kód.
  (Pak ji odeberte: `dotnet ef migrations remove …`.)
