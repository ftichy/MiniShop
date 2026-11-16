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
    /// <summary>
    /// Správa vidí všechny objednávky, zákazník jen své (stejné pravidlo jako policy RequireAdmin). Pro pohledy a akce správy
    /// (detail, zrušení za zákazníka); co dělá zákazník sám za sebe (vratka, platba, „Moje objednávky“), bere
    /// <see cref="ToCustomerOrderAccess"/> – zaměstnanec, který je i zákazník, tak nevrátí cizí objednávku (účty krok 12A).
    /// </summary>
    public static OrderAccess ToOrderAccess(this ClaimsPrincipal user) =>
        // 2FA krok A2: správa bez dvoufázového ověření nevidí cizí objednávky ani tady (pojistka k policy RequireAdmin).
        // Účty krok 12B: všechny objednávky jen role s přehledem objednávek (sklad, podpora, účetní) – stejné role jako
        // politika RequireOrderOverview.
        StaffPolicies.Roles[AuthorizationPolicies.RequireOrderOverview].Any(user.IsInRole)
        && user.HasClaim(AccountClaimTypes.TwoFactorEnabled, "true")
            ? OrderAccess.AllOrders
            : OrderAccess.Customer(user.GetUserId());

    /// <summary>
    /// Účty krok 12B: zrušení objednávky – cizí jen zákaznická podpora (politika RequireSupport), ostatní (i sklad a účetní,
    /// kteří cizí objednávky vidí) jen svou.
    /// </summary>
    public static OrderAccess ToCancelAccess(this ClaimsPrincipal user) =>
        StaffPolicies.Roles[AuthorizationPolicies.RequireSupport].Any(user.IsInRole)
        && user.HasClaim(AccountClaimTypes.TwoFactorEnabled, "true")
            ? OrderAccess.AllOrders
            : OrderAccess.Customer(user.GetUserId());

    /// <summary>Účty krok 12A: jen vlastní objednávky – i pro zaměstnance (akce zákazníka za sebe).</summary>
    public static OrderAccess ToCustomerOrderAccess(this ClaimsPrincipal user) => OrderAccess.Customer(user.GetUserId());

    /// <summary>Účty krok 12A: má aspoň jednu zaměstnaneckou roli.</summary>
    public static bool IsStaff(this ClaimsPrincipal user) => AccountRoles.Staff.Any(user.IsInRole);

    /// <summary>2FA krok A2: zaměstnanec, který si ještě nenastavil dvoufázové ověření – smí jen na jeho nastavení.</summary>
    public static bool NeedsTwoFactorSetup(this ClaimsPrincipal user) =>
        user.IsStaff() && !user.HasClaim(AccountClaimTypes.TwoFactorEnabled, "true");

    /// <summary>Kdo objednává – ID, jméno a e-mail z účtu (ne z formuláře, takže nejdou podvrhnout).</summary>
    public static OrderCustomer ToOrderCustomer(this ClaimsPrincipal user) =>
        new(user.GetUserId(),
            user.FindFirstValue(AccountClaimTypes.DisplayName) ?? user.Identity?.Name ?? string.Empty,
            user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty);

    /// <summary>Účty krok 12C: kdo akci správy dělá – ID účtu pro pravidlo střetu zájmů (vlastní objednávku nevyřizuje).</summary>
    public static string ToActingUserId(this ClaimsPrincipal user) => user.GetUserId();

    /// <summary>E-shop krok 3: čí je košík – ID účtu (jako vlastník objednávky).</summary>
    public static string ToCustomerId(this ClaimsPrincipal user) => user.GetUserId();

    private static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Přihlášený uživatel nemá ID – volá se mimo [Authorize]?");
}
