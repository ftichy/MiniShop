# Účty krok 11B: Povinné dvoufázové ověření admina

Druhá část zadání [issue #94](https://github.com/ftichy/MiniShop/issues/94). Admin může měnit produkty, ceny, vracet peníze
a spravovat uživatele, proto jeho heslo samo nestačí. Audit přihlášení je část B, ZAP v CI část C.

## Jak to funguje

Přirovnání: do skladu smí jen ten, kdo má klíč **i** alarm. Pokud admin alarm (2FA) ještě nemá, dostane se jen k jeho zapnutí.

1. `AccountClaimsPrincipalFactory` přidá do cookie claim `minishop:two_factor`, když má účet zapnuté 2FA.
2. Policy `RequireAdmin` (`AccountsServiceCollectionExtensions`) = role Admin **a** tento claim. Jediné místo pravidla – platí pro
   heslo, Google, `create-admin`, `AdminBootstrap` i povýšeného uživatele (claim se zjišťuje při každém přihlášení).
3. Admin bez 2FA: `OnRedirectToAccessDenied` ho z administrace pošle na `/Account/TwoFactor` (API vrací 403). Stránka mu vysvětlí
   proč (`two-factor-required`). Po zapnutí se cookie obnoví (`RefreshSignInAsync`), administrace se odemkne hned.
4. **Google** už 2FA neobchází (`bypassTwoFactor: false`): kdo má 2FA, zadá po Googlu i kód. Google se jako druhý faktor nepočítá.
5. **Povýšení na admina:** `AccountSignInManager` neuznává „zapamatované zařízení“ u admina, takže cookie z doby zákazníka nepřeskočí kód.
6. **Ztráta telefonu:** záchranný kód, nouzově `reset-2fa --email …` (viz příručka, Accounts) – vypne 2FA, zahodí klíč a kódy,
   nový security stamp, audit `TwoFactorReset` („příkaz na serveru“).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/Accounts/AccountSignInManager.cs` | admin nikdy nepřeskočí kód přes zapamatované zařízení |
| `src/MiniShop.Web/Accounts/ResetTwoFactorCommand.cs` | příkaz `reset-2fa --email …` |
| `src/MiniShop.Infrastructure/Accounts/TwoFactorReset.cs` | služba resetu 2FA (vypnout, nový klíč, prázdné kódy, stamp, audit) |
| `tests/MiniShop.Tests/Web/TwoFactorAdminTests.cs`, `TestTotp.cs` | admin bez 2FA (stránky, akce, API), Google, povýšení, reset |
| `tests/MiniShop.E2E/Totp.cs` | TOTP pro E2E |

## Změněné soubory

- `AccountClaimTypes.TwoFactorEnabled`, `AccountAuditAction.TwoFactorReset` (na konec; text, **bez migrace**), `AccountAuditEntry.ByServerCommand`.
- `AccountsServiceCollectionExtensions` (policy, přesměrování, `AddSignInManager<AccountSignInManager>`), `OrderAccessExtensions` (admin bez 2FA nevidí cizí objednávky).
- `ExternalLogin.cshtml.cs` (2FA po Googlu), `TwoFactor.cshtml(.cs)` (obnovení cookie, vysvětlení pro admina), `Program.cs` (příkaz).
- Testy: `MiniShopWebFactory` (testovací admin má 2FA, `LoginAsync` dopočítá kód, `CreateAdminAsync`), matice (`Admin` + Disable = odepřeno),
  `UserAdministrationTests` (povýšený je bez 2FA zamčený). E2E: `ShopFlow.LoginAdminAsync` – admin si 2FA nastaví přes UI.

## Rozhodnutí

- **Claim, ne middleware:** jedno pravidlo v policy, žádný seznam povolených adres; nové admin stránky jsou chráněné samy.
- **Zapamatované zařízení** se u admina ignoruje podle role při přihlášení – nemusí se nic mazat v cizích prohlížečích.
- **Bez zadních vrátek:** testy i E2E počítají TOTP samy; vývojový admin si 2FA nastaví QR kódem.
- **`ci.yml` beze změny** (agent nesmí měnit workflow) – E2E admina z `create-admin` nastaví 2FA test sám.

## Známá omezení

- Zákazník s 2FA a Googlem teď po Googlu také zadává kód (změna chování A1).
- Starší verze kódu při postupném nasazení neumí přečíst auditní řádek `TwoFactorReset` (jen kdyby se příkaz spustil uprostřed nasazení).
- Nasazení: stávající produkční admin se po nasazení dostane jen na nastavení 2FA.

## Jak ověřit

`dotnet test MiniShop.slnx --filter "FullyQualifiedName~TwoFactor"`; E2E ověří CI. Ručně: přihlásit se jako admin bez 2FA → `/Admin/Users`
přesměruje na `/Account/TwoFactor` → nastavit → administrace funguje; odhlásit, přihlásit (heslo, kód). Před nasazením do produkce:
admin si 2FA nastaví sám, uložit záchranné kódy, ruční záloha.
