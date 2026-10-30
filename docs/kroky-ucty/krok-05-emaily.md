# Účty krok 5: Skutečné e-maily

Do kroku 4 MiniShop „posílal e-maily“ jen do logu. Teď je posílá doopravdy: po registraci odkaz pro potvrzení
e-mailu, na požádání odkaz pro nové heslo a zákazníkovi oznámení o stavu objednávky. V produkci přes Gmail
(`ftichy.gemini@gmail.com`), lokálně do cvičné pošty Mailpit. Plán: [docs/Ucty-roadmap.md](../Ucty-roadmap.md).
(Krok 4 – zrušení objednávky ve stavu Pending – je zatím odložený.)

## Jak to funguje

Přirovnání: Mailpit je cvičná poštovní schránka na stole – dopisy se do ní hodí a dají se přečíst, ale nikam
neodjedou. V produkci se dopisy předávají skutečné poště (Gmail) s plnou mocí (heslem aplikace).

```
 IEmailSender (Application/Abstractions – sdílené jádro)
   ├─ SmtpEmailSender  (MailKit)  ── Email:Host nastavený ──► Gmail (produkce) / Mailpit (Aspire)
   └─ LoggingEmailSender          ── bez Email:Host ──────► jen log (testy, lokální Compose)

 účty (hned v požadavku):   Register ─► AccountEmails.SendConfirmationAsync ─► odkaz /Account/ConfirmEmail?userId&code
                            ForgotPassword ─► SendPasswordResetAsync ─► odkaz /Account/ResetPassword?email&code
                            (token: AddDefaultTokenProviders, 2 h, podepsaný Data Protection, se security stampem)
 objednávky (za outboxem):  OrderConfirmed/Rejected/Completed/Cancelled ─► handler ─► EmailOrderNotifier ─► IEmailSender
```

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Application/Abstractions/IEmailSender.cs` | `IEmailSender` + `EmailMessage` (sdílené jádro) |
| `src/MiniShop.Infrastructure/Email/EmailSettings.cs` | Sekce `Email` (server, port, TLS, přihlášení, odesílatel) |
| `src/MiniShop.Infrastructure/Email/SmtpEmailSender.cs` | `SmtpEmailSender` (MailKit) a `LoggingEmailSender` |
| `src/MiniShop.Infrastructure/Email/EmailServiceCollectionExtensions.cs` | `AddEmail` – SMTP, nebo log podle `Email:Host` |
| `src/MiniShop.Infrastructure/Ordering/EmailOrderNotifier.cs` | Oznámení o objednávce e-mailem (nahrazuje `LoggingOrderNotifier`) |
| `src/MiniShop.Web/Accounts/AccountEmails.cs` | E-maily účtu s odkazem a tokenem (Base64Url) |
| `src/MiniShop.Web/Pages/Account/ConfirmEmail`, `ResendConfirmation`, `ForgotPassword`, `ResetPassword` | Potvrzení e-mailu, poslat znovu, zapomenuté heslo, nové heslo |
| `src/MiniShop.Web/Pages/Shared/_EmailConfirmationBanner.cshtml` | Upozornění „Potvrďte prosím e-mail“ + *Poslat znovu* |
| `tests/MiniShop.Tests/TestDoubles/RecordingEmailSender.cs` | Schránka testů |
| `tests/MiniShop.Tests/Web/AccountEmailTests.cs` | Registrace → potvrzení, poškozený odkaz, poslat znovu, zapomenuté heslo (stejná odpověď), reset jen jednou |
| `tests/MiniShop.Tests/Infrastructure/EmailOrderNotifierTests.cs` | E-mail o objednávce: adresát, položky, cena, důvod zamítnutí |
| `docs/prirucka/architektura/emaily.md` | Stránka příručky o e-mailech |

## Změněné soubory

| Soubor | Změna |
| --- | --- |
| `src/MiniShop.Infrastructure/MiniShop.Infrastructure.csproj` | MailKit 4.18.1; `System.Security.Cryptography.Pkcs` 10.0.13 (oprava zranitelnosti tranzitivní závislosti) |
| `src/MiniShop.Infrastructure/DependencyInjection.cs` | Výchozí `LoggingEmailSender` v `AddInfrastructureCore` |
| `src/MiniShop.Infrastructure/Ordering/OrderingInfrastructure.cs`, `Application/Ordering/IOrderNotifier.cs` | `EmailOrderNotifier`; `LoggingOrderNotifier.cs` smazán |
| `src/MiniShop.Infrastructure/Accounts/AccountClaimsPrincipalFactory.cs`, `CzechIdentityErrorDescriber.cs` | Claim `minishop:email_confirmed`; hláška `InvalidToken` |
| `src/MiniShop.Web/Extensions/AccountsServiceCollectionExtensions.cs` | `AddDefaultTokenProviders`, platnost tokenu 2 h, `AccountEmails` |
| `src/MiniShop.Web/Program.cs`, `appsettings.json` | `AddEmail`, sekce `Email` |
| `src/MiniShop.Web/Pages/Account/Register.cshtml.cs`, `Login.cshtml`, `Shared/_Layout.cshtml` | Potvrzovací e-mail po registraci, odkaz *Zapomenuté heslo?*, upozornění |
| `src/MiniShop.AppHost/AppHost.cs` | Kontejner Mailpit (`axllent/mailpit:v1.31.4`) + `Email__Host/Port/UseTls` pro Web |
| `deploy/compose.yaml`, `deploy/production.env` | `Email__*` z `EMAIL_*` a `SMTP_PASSWORD`; Gmail v produkci |
| `deploy/deploy.sh`, `.github/workflows/deploy.yml` | Nový volitelný secret `SMTP_PASSWORD` (16 malých písmen) |
| `docs/secrets.md` | Řádek `SMTP_PASSWORD` + postup výměny R8 |
| `tests/…/MiniShopWebFactory.cs`, `AuthorizationMatrixTests.cs` | Schránka testů; řádky pro nové stránky |
| `docs/prirucka/…`, `CLAUDE.md`, roadmapa | E-maily, služby (Gmail), běh (Mailpit), knihovny (MailKit), secrets, Ordering, SOLID, historie; krok 4 odložen |

## Rozhodnutí v tomto kroku

- **`IEmailSender` ve sdíleném jádru** – používají ho účty i objednávky; implementace v `Infrastructure/Email`.
- **Bez serveru jen log** – testy a lokální Compose nepotřebují SMTP; nic se omylem neodešle.
- **Mailpit lokálně** (v Aspire) – vývoj e-mailů bez rizika, že dorazí skutečnému člověku.
- **Gmail s heslem aplikace na samostatném účtu** – zdarma, dobrá doručitelnost bez vlastní domény; heslo aplikace
  jde kdykoli zrušit a nedává přístup k hlavnímu účtu.
- **E-maily účtu hned, e-maily objednávek za outboxem** – Accounts outbox nemá; chyba se ukáže uživateli
  (registrace projde i tak, *Poslat znovu*). Objednávky se opakují samy.
- **Token 2 hodiny, jednorázový** (security stamp). **Zapomenuté heslo odpovídá vždy stejně.**
- **Potvrzení e-mailu zatím nepovinné** – jen upozornění. Povinné před objednávkou až v kroku 9.
- **`SMTP_PASSWORD` volitelný** – bez něj produkce běží, e-maily jen do logu (stejně jako `HEALTHCHECKS_BACKUP_URL`).
- **MailKit 4.18.1 a Mailpit v1.31.4** – připnuté verze starší dvou týdnů; Pkcs připnutý kvůli zranitelnosti.

## Známá omezení

- E-mail o objednávce může přijít dvakrát (pád mezi odesláním a zápisem do inboxu).
- Potvrzovací odkaz platí jen 2 hodiny (stejný poskytovatel tokenů jako reset hesla) – pak *Poslat znovu*.
- Bez rate limitingu – kdokoli může opakovaně spouštět *Zapomenuté heslo* (krok 8).
- Admin v produkci (`admin.minishop@gmail.com`) má e-mail nepotvrzený – potvrdí si ho tlačítkem v upozornění.
- E-maily jsou jen text + jednoduché HTML, bez šablon.

## Jak ověřit

```text
dotnet build MiniShop.slnx
dotnet test MiniShop.slnx
dotnet run --project src/MiniShop.AppHost
```

1. V Aspire dashboardu je nový prostředek **mailpit** – otevřete jeho `http` adresu (schránka).
2. Zaregistrujte se → nahoře upozornění „Potvrďte prosím e-mail“; v Mailpitu e-mail s odkazem → klikněte →
   „E-mail je potvrzený“, upozornění zmizí.
3. Odhlaste se → *Přihlásit* → *Zapomenuté heslo?* → e-mail v Mailpitu → nastavte nové heslo → přihlaste se jím.
   Stejný odkaz podruhé → „Odkaz je neplatný, už byl použitý nebo vypršel“.
4. Vytvořte objednávku → za pár sekund v Mailpitu „Objednávka č. … je potvrzená“ s položkami a cenou.

### Produkce (ručně, po krocích – PŘED sloučením PR)

1. Účet `ftichy.gemini@gmail.com`: zapnout dvoufázové ověření.
2. https://myaccount.google.com/apppasswords → heslo aplikace „MiniShop“ (16 písmen – zapsat **bez mezer**).
3. GitHub → *Settings → Environments → production* → secret `SMTP_PASSWORD`.
4. Nový `deploy.sh` na VM (nový `deploy.yml` posílá řádek `SMTP_PASSWORD=` vždy – starý skript by ho odmítl):
   ```text
   curl -fsSL https://raw.githubusercontent.com/ftichy/MiniShop/<sha-větve>/deploy/deploy.sh -o /tmp/minishop-deploy
   sudo install -m 755 -o root -g root /tmp/minishop-deploy /usr/local/bin/minishop-deploy
   ```
5. Sloučit PR → nasazení → na webu *Zapomenuté heslo* pro svůj účet → e-mail dorazí do skutečné schránky.

## Vyzkoušejte sami

- V Mailpitu otevřete zdroj e-mailu (*Raw*) – najdete hlavičky `From`, `To`, text i HTML verzi.
- Na stránce *Zapomenuté heslo* zadejte neexistující e-mail. Liší se odpověď? A v Mailpitu?
- Upravte v odkazu z e-mailu jeden znak tokenu. Co řekne stránka a proč (podpis)?
- Zastavte kontejner mailpit a vytvořte objednávku. Co udělá outbox? (log Webu, pak mailpit znovu spusťte)
