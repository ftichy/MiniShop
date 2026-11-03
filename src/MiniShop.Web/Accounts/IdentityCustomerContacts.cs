using Microsoft.AspNetCore.Identity;
using MiniShop.Application.Notifications;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// E-shop krok 10: kontakt a souhlasy zákazníka pro modul Notifications – přímo z účtů (users.db). Moduly se neznají,
/// spojí je až composition root (Web hostuje oba).
/// </summary>
public sealed class IdentityCustomerContacts(UserManager<ApplicationUser> users) : ICustomerContacts
{
    public async Task<CustomerContact?> FindAsync(string customerId, CancellationToken cancellationToken = default) =>
        await users.FindByIdAsync(customerId) is { } user
            ? new CustomerContact(user.Email ?? string.Empty, user.DisplayName, user.EmailConfirmed, user.CartRemindersConsent)
            : null;
}
