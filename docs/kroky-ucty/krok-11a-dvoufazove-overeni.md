# Účty krok 11A: Dvoufázové ověření (2FA, dobrovolné)

První část zadání [issue #94](https://github.com/ftichy/MiniShop/issues/94) (zabezpečení). Zákazník si může zapnout
dvoufázové ověření kódem z aplikace v telefonu (TOTP). Povinnost pro admina je část A2, audit přihlášení část B, ZAP v CI část C.

## Jak to funguje

Přirovnání: heslo je klíč od bytu, kód z telefonu (mění se každých 30 s) je alarm, který bez telefonu nevypnete.

1. `/Account/TwoFactor` (každý přihlášený): tajný klíč textem + **QR kód jako SVG vykreslené na serveru** (QRCoder).
   Zapnutí až po prvním platném kódu → zobrazí se 10 záchranných kódů (jednou).
2. `Login` po správném hesle dostane z Identity `RequiresTwoFactor` → přesměrování na `/Account/LoginWith2fa`
   (nebo `/Account/LoginWithRecoveryCode`). Špatný kód se počítá do zámku účtu (5 × → 15 min).
3. „Pamatovat toto zařízení“ jen pro zákazníka (`TwoFactorHelper.MayRememberDevice` – pro A2 stačí pravidlo upravit).
4. Vypnutí a nové záchranné kódy jen s platným kódem z aplikace; admin vypnout nesmí (`MayDisable`, server vrací 403).

## Nové soubory

| Soubor | Co dělá |
| --- | --- |
| `src/MiniShop.Web/Accounts/TwoFactorHelper.cs` | formát klíče, `otpauth://` adresa, QR jako SVG, pravidla (zapamatování, vypnutí), úprava kódů |
| `src/MiniShop.Web/Pages/Account/TwoFactor.cshtml(.cs)` | zapnutí, záchranné kódy, vypnutí |
| `src/MiniShop.Web/Pages/Account/LoginWith2fa.cshtml(.cs)` | druhý krok přihlášení kódem |
| `src/MiniShop.Web/Pages/Account/LoginWithRecoveryCode.cshtml(.cs)` | druhý krok záchranným kódem |
| `tests/MiniShop.Tests/Web/TwoFactorTests.cs` | zapnutí, přihlášení, záchranný kód (jednou), zámek, admin, log |

## Změněné soubory

- `MiniShop.Web.csproj` – balíček `QRCoder` 1.6.0 (MIT); katalog třetích stran (`knihovny.md`).
- `Login.cshtml.cs` – `RequiresTwoFactor` → přesměrování; `_AccountFooter.cshtml` – odkaz; `site.css` – `.two-factor-qr`.
- `AuthorizationMatrixTests` – `/Account/TwoFactor` + handlery Enable/Disable/Regenerate.
- Příručka: `architektura/moduly/accounts.md` (sekce 2FA).

## Rozhodnutí

- **Žádná vlastní kryptografie** – Identity (`UserManager`/`SignInManager`), tabulky už existují → **bez migrace**.
- **QR na serveru** (SVG v HTML) – CSP `script-src 'self'` a `img-src` beze změny, klíč nikam neodchází.
- **Záchranný kód se nenormalizuje pomlčkou** (je součástí kódu `ABCDE-12345`), kód z aplikace ano.
- **Stránky s kódem jsou `[AllowAnonymous]`**, ale bez hesla v předchozím kroku se samy vrátí na přihlášení – proto nejsou v matici oprávnění (hlídá je `TwoFactorTests`).

## Známá omezení

- Přihlášení přes Google 2FA stále obchází (`bypassTwoFactor: true`) – řeší A2 autorizací admina.
- Povýšený uživatel může mít „zapamatované zařízení“ z doby, kdy byl zákazník – A2 musí `ForgetTwoFactorClientAsync`/ověřit při povýšení.
- Postup při ztrátě telefonu u admina (příkaz `reset-2fa`) přijde s A2.

## Jak ověřit

`dotnet test MiniShop.slnx --filter "FullyQualifiedName~TwoFactorTests"`. Ručně: `/Account/TwoFactor` → naskenovat QR →
zadat kód → uložit záchranné kódy → odhlásit → přihlásit (heslo, pak kód).

## Oprava po nasazení

QR kód nešel naskenovat telefonem (ruční opsání klíče fungovalo): SVG z QRCoderu mělo pevné `width`/`height` 342 px
bez `viewBox`, CSS ho zúžilo na 260 px a obsah se **ořízl** (chyběl roh a bílý okraj). Oprava: `SizingMode.ViewBoxAttribute`
v `TwoFactorHelper.QrCodeSvg`, test `QrKod_SeZmensiCely_MaViewBoxBezPevneSirky`. Poučení: vzhled, který agent nevidí
(nemá prohlížeč), ověřit snímkem nebo na telefonu před sloučením.
