# Účty krok 9: Otevření webu

MiniShop je od tohoto kroku **veřejný** – Caddy už nechce heslo (basic auth zůstal jen před Aspire dashboardem).
Spolu s otevřením přibyla ochrana registrace (objednávat jde jen s potvrzeným e-mailem) a GDPR minimum: uživatel si
může smazat účet a stránka Ochrana osobních údajů říká, co se ukládá. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).

## Jak to funguje

Přirovnání: obchod otevřel dveře do ulice. Prohlížet může každý, nakupovat jen ten, kdo prokázal, že adresa
na objednávce je jeho. Kdo odejde, může si nechat skartovat zákaznickou kartu – účtenky zůstanou, ale bez jména.

```
 Caddy (BASIC_AUTH=off)  web ─► bez hesla        dashboard.… ─► vždy basic auth       Server/Via hlavičky pryč

 objednávka:   RequireCustomer = role User + claim minishop:email_confirmed
               nepotvrzený → místo formuláře „Objednávat můžete, až potvrdíte svůj e-mail“; API 403

 smazání účtu: /Account/Delete ─► CheckPasswordSignInAsync (počítá se do zámku)
                               ─► IOrderService.AnonymizeCustomerAsync (ordering.db)
                                    nevyřízená objednávka → DomainException, nic se nezmění
                                    jinak Order.Anonymize: „Smazaný zákazník“, e-mail prázdný
                               ─► AccountDeletion.DeleteAsync (users.db, transakce)
                                    admin → odmítnuto; audit ForgetUser → „smazaný účet“; DeleteAsync (role, claimy cascade)
                               ─► SignOut → úvod „Účet je smazaný“
```

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Infrastructure/Accounts/AccountDeletion.cs` | Smazání účtu: pravidlo „admin ne“, anonymizace auditu, smazání |
| `src/MiniShop.Web/Pages/Account/Delete.cshtml(.cs)` | Stránka *Smazat účet* (heslo, pořadí Ordering → Accounts) |
| `src/MiniShop.Web/Pages/Privacy.cshtml` | Ochrana osobních údajů (veřejná) |
| `tests/MiniShop.Tests/Web/AccountDeletionTests.cs` | Nepotvrzený e-mail neobjedná, smazání (objednávky, audit, přihlášení), nevyřízená objednávka, špatné heslo, admin, soukromí |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `deploy/production.env` | `BASIC_AUTH=off` |
| `deploy/Caddyfile` | Snippet `hide-server` (bez `Server` a `Via`) pro web i dashboard |
| `.github/workflows/deploy.yml` | Smoke test: úvod 200, dashboard 401 |
| `src/MiniShop.Web/Program.cs`, `src/MiniShop.Inventory.Api/Program.cs` | Kestrel bez hlavičky `Server` |
| `src/MiniShop.Web/Extensions/AccountsServiceCollectionExtensions.cs`, `Accounts/AuthorizationPolicies.cs` | `RequireCustomer` + potvrzený e-mail |
| `src/MiniShop.Web/Pages/Orders/Index.cshtml(.cs)` | Výzva k potvrzení e-mailu místo formuláře |
| `src/MiniShop.Domain/Orders/Order.cs` | `IsActive`, `Anonymize`, `AnonymizedCustomerName` |
| `src/MiniShop.Application/Ordering/IOrderService.cs`, `OrderService.cs` | `AnonymizeCustomerAsync` |
| `src/MiniShop.Infrastructure/Ordering/EmailOrderNotifier.cs` | Na prázdnou adresu nepíše |
| `src/MiniShop.Infrastructure/Accounts/AccountAuditEntry.cs`, `AccountsInfrastructure.cs` | `ForgetUser`; registrace `AccountDeletion` |
| `src/MiniShop.Web/Pages/Shared/_Layout.cshtml`, `Index.cshtml`, `Account/Register.cshtml`, `Account/ChangePassword.cshtml` | Patička s odkazem na soukromí, zpráva po smazání, odkaz *Smazat účet* |
| `tests/…/MiniShopWebFactory.cs`, `AuthorizationMatrixTests.cs`, `Domain/OrderTests.cs` | Potvrzený e-mail u testovacích uživatelů, zrušená objednávka; řádky matice; anonymizace |
| `docs/prirucka/…`, `docs/secrets.md`, `CLAUDE.md`, roadmapa | Accounts (otevřený web), Ordering, e-maily, HTTPS a přístup, secrets, historie; CAPTCHA odložena |

## Rozhodnutí v tomto kroku

- **CAPTCHA (Turnstile) odložena** – registraci brzdí rate limiting (krok 8) a objednat jde jen s potvrzeným e-mailem;
  Turnstile by přidal cizí skript (výjimka v CSP), secret a Cloudflare by viděl návštěvníky.
- **Potvrzený e-mail jako claim v policy** – platí pro stránku i API stejně; claim se obnoví hned po potvrzení
  (`RefreshSignInAsync`), jinak nejpozději do minuty (security stamp validator).
- **Nevyřízená objednávka brání smazání** – nevznikne objednávka bez kontaktu, kterou by admin musel vyřídit.
- **Admin si účet smazat nemůže** – pravidlo „vždy zůstane admin“ drží bez dalších výjimek.
- **Pořadí Ordering → Accounts** – selhání v půlce nenechá osobní údaje na objednávkách smazaného účtu.
- **Objednávky se anonymizují, nemažou** – položky a ceny jsou potřeba pro účetnictví a statistiky.
  `CustomerId` zůstává (po smazání účtu nikoho neidentifikuje).
- **Audit** – výjimka z „jen přidávat“: e-mail smazaného uživatele se přepíše (právo být zapomenut je silnější).
- **Heslo při smazání** – kdo jen našel otevřený prohlížeč, účet nesmaže; pokusy se počítají do zámku účtu.

## Známá omezení

- Smazané údaje zůstávají v nočních zálohách až 7 dní (uvedeno na stránce soukromí).
- Pending objednávka účet blokuje, dokud ji sága nevyřeší (sekundy); zrušit Pending zatím nejde (krok 4 odložen).
- Výpis údajů (právo na přístup) a opravu e-mailu řeší zatím e-mail na `ftichy.gemini@gmail.com`.
- Starší produkční účty s nepotvrzeným e-mailem nemohou objednávat, dokud e-mail nepotvrdí.
- Bez CAPTCHA – roboti se můžou registrovat (objednat ale bez potvrzeného e-mailu nemohou).

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. Zaregistrujte se → *Objednávky*: místo formuláře výzva k potvrzení. Potvrďte e-mail (Mailpit) → formulář je tu.
2. Objednejte, nechte potvrdit a zrušte. Jméno vpravo nahoře → *Smazat účet* → heslo → „Účet je smazaný“.
3. Jako admin v *Objednávky*: u té objednávky „Smazaný zákazník“; v *Uživatelé* účet chybí.
4. Patička → *Ochrana osobních údajů*.

### Produkce

Nic ručního. Po nasazení: https://minishop-tichy.duckdns.org se otevře **bez hesla** (smoke test kontroluje 200),
https://dashboard.minishop-tichy.duckdns.org pořád heslo chce (smoke test kontroluje 401). V F12 už není `Server` ani `Via`.

## Vyzkoušejte sami

- Zkuste `POST /api/orders` s cookie nepotvrzeného účtu (Postman). Jaký status a proč ne 401?
- Proč je `CustomerId` po smazání účtu v pořádku nechat, ale e-mail ne? (Nápověda: co ještě o ID víme?)
- https://securityheaders.com a https://www.ssllabs.com/ssltest/ – teď, když je web veřejný, jakou dostane známku?
