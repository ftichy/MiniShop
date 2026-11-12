using System.Security.Claims;
using MiniShop.Application.Ordering;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 3: převod přihlášeného uživatele (claims z cookie) na pojmy Orderingu. Ordering tak zná jen ID
/// zákazníka – o účtech, rolích ani cookie neví nic.
/// </summary>
public static class OrderAccessExtensions
{
    /// <summary>Admin vidí všechny objednávky, zákazník jen své (stejné pravidlo jako policy RequireAdmin).</summary>
    public static OrderAccess ToOrderAccess(this ClaimsPrincipal user) =>
        // 2FA krok A2: admin bez dvoufázového ověření nevidí cizí objednávky ani tady (pojistka k policy RequireAdmin).
        user.IsInRole(AccountRoles.Admin) && user.HasClaim(AccountClaimTypes.TwoFactorEnabled, "true")
            ? OrderAccess.AllOrders
            : OrderAccess.Customer(user.GetUserId());

    /// <summary>2FA krok A2: admin, který si ještě nenastavil dvoufázové ověření – smí jen na jeho nastavení.</summary>
    public static bool NeedsTwoFactorSetup(this ClaimsPrincipal user) =>
        user.IsInRole(AccountRoles.Admin) && !user.HasClaim(AccountClaimTypes.TwoFactorEnabled, "true");

    /// <summary>Kdo objednává – ID, jméno a e-mail z účtu (ne z formuláře, takže nejdou podvrhnout).</summary>
    public static OrderCustomer ToOrderCustomer(this ClaimsPrincipal user) =>
        new(user.GetUserId(),
            user.FindFirstValue(AccountClaimTypes.DisplayName) ?? user.Identity?.Name ?? string.Empty,
            user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty);

    /// <summary>E-shop krok 3: čí je košík – ID účtu (jako vlastník objednávky).</summary>
    public static string ToCustomerId(this ClaimsPrincipal user) => user.GetUserId();

    private static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Přihlášený uživatel nemá ID – volá se mimo [Authorize]?");
}
