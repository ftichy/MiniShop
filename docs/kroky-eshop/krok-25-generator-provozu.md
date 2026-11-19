# E-shop krok 25: Generátor provozu

Issue #155. Potřeba: mít v Grafaně co sledovat – ale sám nevygeneruješ víc souběžných objednávek. Řešení: konzolový
generátor, který z tvého PC předvede několik zákazníků a skladníka přes HTTP. Přirovnání: figuranti v zkušebním obchodě.

## Jak to funguje

1. Příkaz `create-loadtest-users` (vzor `create-admin`) založí zákazníky `loadtest-N@loadtest.minishop.invalid` s potvrzeným
   e-mailem a skladníka `loadtest-sklad@…` s rolí Warehouse a zapnutým 2FA (klíč vypíše).
2. `SmtpEmailSender` neodešle nic na adresu končící `.invalid` – e-maily o objednávkách i fakturách testovacích účtů
   nikam nejdou (nikdy skutečná schránka ani Gmail).
3. `tools/MiniShop.TrafficGenerator`: každý zákazník má vlastní `Session` (cookies, antiforgery token) a prochází scénáře
   doručená / zrušená / vrácená, vždy s dobírkou. Skladník (`Desk`, přihlášení heslem + TOTP) objednávky zabalí, odešle,
   doručí a převezme vratku. Čekání na ságy = opakované načtení detailu (žádné pevné spaní).
4. Na konci tabulka s časem do potvrzení a výsledkem; chyba = návratový kód 1.

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/LoadTestAccountSeeder.cs` | idempotentní založení testovacích účtů + 2FA skladníka |
| `src/MiniShop.Web/Accounts/CreateLoadTestUsersCommand.cs` | příkaz `create-loadtest-users` |
| `tools/MiniShop.TrafficGenerator/*` | generátor (`Program`, `Session`, `Shop`, `Desk`, `HtmlForms`, `Totp`, `Options`) |
| `tests/MiniShop.Tests/Infrastructure/LoadTestAccountSeederTests.cs` | účty, role, 2FA, idempotence |
| `docs/prirucka/nasazeni/generator-provozu.md` | příručka |

## Změněné soubory

`Program.cs` Webu (příkaz), `AccountsInfrastructure.cs` (registrace), `SmtpEmailSender.cs` (`.invalid`),
`CreateAdminCommand.cs` (`ReadPassword` internal), `MiniShop.slnx` (složka tools).

## Rozhodnutí

- Ručně z PC, ne tlačítko v aplikaci (žádný testovací kód v produkci, žádný zásah do workflow).
- Jen dobírka – Stripe by vyžadoval prohlížeč.
- Žádné nové kontrakty, migrace ani secrets. Testovací objednávky zůstávají v tržbách i fakturách (výslovné přání).
- Skladník má nejmenší potřebnou roli, ne správce.

## Známá omezení

Rate limiting formulářů účtu (429), generátor závisí na názvech polí formulářů, účty se nemažou.
**Nebylo ověřeno proti běžící aplikaci** (agent v CI nemá spuštěný web) – první spuštění proti lokálnímu AppHostu doporučuji
před produkcí.

## Jak ověřit

1. `dotnet run --project src/MiniShop.AppHost`, pak `dotnet run --project src/MiniShop.Web -- create-loadtest-users`.
2. Spustit generátor s `--url https://localhost:7180 --pause 1`; všechny scénáře mají skončit `OK`.
3. V Grafaně / Aspire dashboardu najít trasy podle `minishop.order.id` z tabulky.
