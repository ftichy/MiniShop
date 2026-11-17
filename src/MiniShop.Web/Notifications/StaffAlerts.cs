using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MiniShop.Application.Notifications;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Domain.Notifications;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Notifications;

/// <summary>
/// E-shop krok 24: který typ oznámení k práci patří ke které roli – jediné místo (Notifications role nezná).
/// Nový typ = hodnota v <see cref="StaffAlertKind"/> + řádek tady + popis na stránce E-maily a oznámení.
/// </summary>
public static class StaffAlertRoles
{
    public static readonly IReadOnlyDictionary<StaffAlertKind, string[]> Roles = new Dictionary<StaffAlertKind, string[]>
    {
        [StaffAlertKind.NewOrder] = [AccountRoles.Warehouse],
        [StaffAlertKind.ReturnRequested] = [AccountRoles.CustomerSupport],
        [StaffAlertKind.OutOfStock] = [AccountRoles.Warehouse, AccountRoles.CatalogManager]
    };

    /// <summary>Popisky na stránce nastavení.</summary>
    public static readonly IReadOnlyDictionary<StaffAlertKind, string> Labels = new Dictionary<StaffAlertKind, string>
    {
        [StaffAlertKind.NewOrder] = "Nová objednávka k zabalení",
        [StaffAlertKind.ReturnRequested] = "Žádost zákazníka o vratku",
        [StaffAlertKind.OutOfStock] = "Došlo zboží (produkt je vyprodaný)"
    };

    /// <summary>Typy, ke kterým má přihlášený uživatel roli (z cookie – jen pro zobrazení; při odeslání rozhoduje DB).</summary>
    public static IReadOnlyList<StaffAlertKind> AllowedFor(ClaimsPrincipal user) =>
        Roles.Where(r => r.Value.Any(user.IsInRole)).Select(r => r.Key).ToList();
}

/// <summary>
/// E-shop krok 24: <see cref="IStaffDirectory"/> nad účty – při odeslání ověří v databázi, že účet existuje, není
/// zablokovaný a má k typu oznámení roli (odebraná role = žádné oznámení, i když zůstala zaškrtnutá volba).
/// </summary>
public sealed class IdentityStaffDirectory(UserManager<ApplicationUser> users) : IStaffDirectory
{
    public async Task<IReadOnlyList<string>> FilterAllowedAsync(StaffAlertKind kind, IReadOnlyList<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var roles = StaffAlertRoles.Roles[kind];
        var allowed = new List<string>();
        foreach (var userId in userIds.Distinct())
        {
            if (await users.FindByIdAsync(userId) is not { } user
                || user.LockoutEnd?.Year == DateTimeOffset.MaxValue.Year)   // zablokovaný správcem (AccountAdministration)
            {
                continue;
            }

            var userRoles = await users.GetRolesAsync(user);
            if (roles.Any(userRoles.Contains))
            {
                allowed.Add(userId);
            }
        }

        return allowed;
    }
}

/// <summary>E-shop krok 24: název produktu z kopie katalogu Orderingu (spojí je composition root).</summary>
public sealed class CatalogProductNames(ICatalogQueryService catalog) : IProductNames
{
    public async Task<string?> FindAsync(int productId, CancellationToken cancellationToken = default) =>
        (await catalog.GetProductAsync(productId, cancellationToken))?.Name;
}
