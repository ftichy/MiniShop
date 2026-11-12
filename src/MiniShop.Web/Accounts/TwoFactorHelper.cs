using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using MiniShop.Infrastructure.Accounts;
using QRCoder;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Dvoufázové ověření (TOTP): pomocné funkce pro stránky. Kryptografii (klíč, ověření kódu, záchranné kódy) dělá Identity,
/// tady je jen tvar textu a obrázku.
/// </summary>
public static partial class TwoFactorHelper
{
    public const string Issuer = "MiniShop";

    /// <summary>Počet záchranných kódů při vygenerování (výchozí hodnota Identity).</summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>
    /// Smí si zařízení pamatovat? Zákazník ano, admin ne – admin zadává kód při každém přihlášení.
    /// Jediné místo pravidla (zapamatování zařízení je v A1 jen u zákazníků).
    /// </summary>
    public static bool MayRememberDevice(IEnumerable<string> roles) => !roles.Contains(AccountRoles.Admin);

    /// <summary>Smí si 2FA vypnout? Zákazník ano, admin ne (účet admina musí zůstat chráněný).</summary>
    public static bool MayDisable(IEnumerable<string> roles) => !roles.Contains(AccountRoles.Admin);

    /// <summary>Kód z aplikace bez mezer a pomlček (aplikace ho zobrazují jako „123 456“).</summary>
    public static string NormalizeCode(string? code) =>
        code is null ? string.Empty : code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    /// <summary>Záchranný kód: jen bez mezer – pomlčka je jeho součástí („ABCDE-12345“), Identity ho porovnává i s ní.</summary>
    public static string NormalizeRecoveryCode(string? code) =>
        code is null ? string.Empty : code.Replace(" ", string.Empty).Trim();

    /// <summary>Tajný klíč po čtyřech znacích, malými písmeny – lépe se opisuje ručně.</summary>
    public static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        var position = 0;
        while (position + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(position, 4)).Append(' ');
            position += 4;
        }

        if (position < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(position));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>Adresa pro aplikaci s ověřovacími kódy (otpauth://), kterou kóduje QR.</summary>
    public static string AuthenticatorUri(string email, string unformattedKey) =>
        string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={unformattedKey}&issuer={Uri.EscapeDataString(Issuer)}&digits=6");

    /// <summary>
    /// QR kód jako SVG vykreslené na serveru – CSP <c>script-src 'self'</c> neumí inline skript ani CDN a obrázek
    /// se nikam neposílá (tajný klíč nesmí opustit server jinak než v odpovědi uživateli).
    /// </summary>
    public static string QrCodeSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        // viewBox místo pevných width/height: CSS (.two-factor-qr, max. 260 px) kód zmenší celý. S pevnou šířkou 342 px
        // by se jen ořízl – chyběl by roh a bílý okraj a telefon by QR nenačetl (opraveno po nasazení A1).
        var svg = new SvgQRCode(data).GetGraphic(6, "#000000", "#FFFFFF", drawQuietZones: true,
            sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        // Do HTML patří jen element <svg>, ne XML hlavička.
        var start = svg.IndexOf("<svg", StringComparison.Ordinal);
        return start > 0 ? svg[start..] : svg;
    }

    [GeneratedRegex("^[0-9]{6}$")]
    public static partial Regex SixDigits();
}
