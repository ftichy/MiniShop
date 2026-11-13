# Účty krok 11C: Audit přihlášení

Třetí část zadání [issue #94](https://github.com/ftichy/MiniShop/issues/94). Cíl: dohledat, kdo se kdy (ne)přihlásil, aby šel odhalit
podezřelý pokus (hádání hesla, zkoušení cizích účtů). Zbývá část C (OWASP ZAP v CI).

## Jak to funguje

Přirovnání: vrátný zapisuje do sešitu každého, kdo zazvonil – i toho, koho nepustil.

1. Každá cesta přihlášení (`Login`, `LoginWith2fa`, `LoginWithRecoveryCode`, `ExternalLogin` pro Google) po rozhodnutí `SignInManageru`
   zavolá `LoginAttemptRecorder.RecordAsync`. `LoginAudit.Map` převede `SignInResult` na `LoginResult` (zámek vs. zablokováno adminem
   se pozná podle `LockoutEnd` v roce 9999).
2. Záznam obsahuje ID účtu (neznámý e-mail → `null`), metodu, výsledek, čas a IP. **E-mail ani heslo ne.**
3. Jedno přihlášení s 2FA = dva záznamy: heslo (`AwaitingSecondFactor`) a kód (`Succeeded`/`WrongCode`).
4. Zápis nikdy nevyhodí výjimku – při chybě DB se jen zaloguje typ chyby a přihlášení projde.
5. Admin vidí posledních 20 pokusů na `/Admin/Users/Detail/{id}`, uživatel své na `/Account/LoginHistory`.
6. `LoginAttemptRetentionWorker` (jen vedoucí kopie) po startu a pak hodinově maže záznamy starší 90 dní; smazání účtu je maže hned.

## Nové soubory

- `Infrastructure/Accounts/LoginAttempt.cs`, `LoginAttemptRecorder.cs`, migrace `…_LoginAttempts` (jen nová tabulka)
- `Web/Accounts/LoginAudit.cs`, `Web/BackgroundServices/LoginAttemptRetentionWorker.cs`
- `Web/Pages/Account/LoginHistory.cshtml(.cs)`, `Web/Pages/Shared/_LoginAttempts.cshtml`
- `tests/MiniShop.Tests/Web/LoginAuditTests.cs`

## Změněné soubory

`AccountsDbContext`, `AccountsInfrastructure`, `AccountDeletion`, stránky `Login`, `LoginWith2fa`, `LoginWithRecoveryCode`,
`ExternalLogin`, `Admin/Users/Detail`, `_AccountFooter`, `Program.cs`, `/Privacy`, příručka `accounts.md`, `AuthorizationMatrixTests`,
`WorkerRegistrationTests`, CLAUDE.md.

## Rozhodnutí

- **Nová tabulka místo `AccountAuditEntries`:** ta má povinné e-maily a slouží auditu správy účtů; pro přihlášení se nehodí.
- **Bez cizího klíče na uživatele:** neznámý e-mail nemá účet; smazání účtu maže záznamy explicitně v téže transakci.
- **Záznam „první krok prošel“:** odlišuje heslo správné od přihlášení dokončeného – útočník, který zná heslo, ale nemá kód, je vidět.
- **Retence 90 dní:** IP je osobní údaj (uvedeno na `/Privacy`).

## Známá omezení

- Cizí IP u přihlášení za proxy je správně jen tehdy, když Caddy posílá `X-Forwarded-For` a je zapnuté `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (v Compose je).
- Záznamy se mažou po hodině od startu vedoucí kopie, ne přesně po 90 dnech.
- Neúspěšný pokus při pádu DB se ztratí (jen log) – záměr, aby audit nerozbil přihlášení.
- Rate-limit (429) odmítne požadavek dřív, než dojde na přihlášení – takový pokus se nezapíše.

## Jak ověřit

- `dotnet test MiniShop.slnx` (`LoginAuditTests`).
- Ručně: přihlaste se špatným heslem a pak správným → `/Account/LoginHistory` ukáže oba pokusy; admin je vidí na detailu uživatele.
- **V produkci ověřit IP:** přihlaste se z telefonu přes mobilní data – na `/Account/LoginHistory` musí být vaše veřejná IP, ne adresa
  z Docker sítě (`172.x`).

## Vyzkoušejte sami

1. Pětkrát zadejte špatné heslo – pátý záznam je „účet zamčený“.
2. Zapněte 2FA a přihlaste se se špatným kódem – uvidíte „špatný kód“.
3. Admin: zablokujte uživatele a zkuste se za něj přihlásit – „účet zablokovaný“.
