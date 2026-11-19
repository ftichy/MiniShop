using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Výsledek <see cref="LoadTestAccountSeeder.SeedAsync"/> – pro výpis v terminálu.</summary>
public sealed record LoadTestAccountsResult(
    IReadOnlyList<string> CustomerEmails, string StaffEmail, string StaffTotpKey, int Created, IReadOnlyList<string> Errors);

/// <summary>
/// Generátor provozu (issue #155): testovací účty pro <c>tools/MiniShop.TrafficGenerator</c>. Zákazníci
/// <c>loadtest-N@…invalid</c> mají potvrzený e-mail (bez schránky se jinak nedají potvrdit) a účet skladníka
/// <c>loadtest-sklad@…</c> má jedinou roli Warehouse (expedice a převzetí vratky) a zapnuté 2FA – klíč se vypíše.
/// </summary>
/// <remarks>
/// Doména <c>.invalid</c> je podle RFC 2606 vyhrazená pro neexistující adresy a <c>SmtpEmailSender</c> na ni nic neodešle,
/// takže testovací provoz nikdy nezatíží skutečnou schránku ani Gmail. Příkaz je idempotentní (existující účet přeskočí).
/// </remarks>
public sealed class LoadTestAccountSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ILogger<LoadTestAccountSeeder> logger)
{
    public const string EmailDomain = "loadtest.minishop.invalid";

    public static string CustomerEmail(int number) => $"loadtest-{number}@{EmailDomain}";

    public static string StaffEmail => $"loadtest-sklad@{EmailDomain}";

    public async Task<LoadTestAccountsResult> SeedAsync(int customerCount, string password)
    {
        foreach (var role in new[] { AccountRoles.User, AccountRoles.Warehouse })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var errors = new List<string>();
        var created = 0;
        var emails = new List<string>();

        for (var number = 1; number <= customerCount; number++)
        {
            var email = CustomerEmail(number);
            emails.Add(email);
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true, DisplayName = $"Test Zákazník {number}"
            };
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                result = await userManager.AddToRoleAsync(user, AccountRoles.User);
            }

            if (result.Succeeded)
            {
                created++;
            }
            else
            {
                errors.AddRange(result.Errors.Select(error => error.Description));
            }
        }

        var staff = await userManager.FindByEmailAsync(StaffEmail);
        if (staff is null)
        {
            staff = new ApplicationUser
            {
                UserName = StaffEmail, Email = StaffEmail, EmailConfirmed = true, DisplayName = "Test Skladník"
            };
            var result = await userManager.CreateAsync(staff, password);
            if (result.Succeeded)
            {
                result = await userManager.AddToRoleAsync(staff, AccountRoles.Warehouse);
            }

            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors.Select(error => error.Description));
                return new(emails, StaffEmail, string.Empty, created, errors);
            }

            created++;
        }

        // 2FA: stejné kroky jako na stránce /Account/TwoFactor, jen bez telefonu – klíč si generátor spočítá sám.
        if (await userManager.GetAuthenticatorKeyAsync(staff) is null)
        {
            await userManager.ResetAuthenticatorKeyAsync(staff);
        }

        await userManager.SetTwoFactorEnabledAsync(staff, true);
        var key = await userManager.GetAuthenticatorKeyAsync(staff) ?? string.Empty;

        logger.LogInformation("Testovací účty generátoru provozu: nově {Created}", created);
        return new(emails, StaffEmail, key, created, errors);
    }
}
