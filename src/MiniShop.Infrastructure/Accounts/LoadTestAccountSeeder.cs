using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Výsledek <see cref="LoadTestAccountSeeder.SeedAsync"/> – pro výpis v terminálu.</summary>
public sealed record LoadTestAccountsResult(
    IReadOnlyList<string> CustomerEmails, string StaffEmail, string StaffTotpKey, int Created, IReadOnlyList<string> Errors)
{
    /// <summary>Existující účty, kterým příkaz nastavil zadané heslo (a zrušil blokaci).</summary>
    public int Reset { get; init; }
}

/// <summary>
/// Generátor provozu (issue #155): testovací účty pro <c>tools/MiniShop.TrafficGenerator</c>. Zákazníci
/// <c>loadtest-N@…invalid</c> mají potvrzený e-mail (bez schránky se jinak nedají potvrdit) a účet skladníka
/// <c>loadtest-sklad@…</c> má jedinou roli Warehouse (expedice a převzetí vratky) a zapnuté 2FA – klíč se vypíše.
/// </summary>
/// <remarks>
/// Doména <c>.invalid</c> je podle RFC 2606 vyhrazená pro neexistující adresy a <c>SmtpEmailSender</c> na ni nic neodešle,
/// takže testovací provoz nikdy nezatíží skutečnou schránku ani Gmail. Příkaz je idempotentní: chybějící účty založí,
/// existujícím testovacím účtům nastaví zadané heslo a zruší blokaci (po testu se doporučuje je zablokovat) – po každém
/// spuštění mají všechny stejné heslo. Klíč 2FA skladníka se nemění. Jiných účtů než <c>loadtest-…@loadtest.minishop.invalid</c>
/// se nedotkne.
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
        var emails = Enumerable.Range(1, customerCount).Select(CustomerEmail).ToList();

        // Heslo ověřit předem: jinak by část účtů vznikla a část ne (nebo by existující účet zůstal bez hesla).
        var invalid = await ValidatePasswordAsync(password);
        if (invalid.Count > 0)
        {
            return new(emails, StaffEmail, string.Empty, 0, invalid);
        }

        foreach (var role in new[] { AccountRoles.User, AccountRoles.Warehouse })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var errors = new List<string>();
        var created = 0;
        var reset = 0;

        for (var number = 1; number <= customerCount; number++)
        {
            var outcome = await EnsureAccountAsync(emails[number - 1], $"Test Zákazník {number}", AccountRoles.User, password, errors);
            created += outcome == Outcome.Created ? 1 : 0;
            reset += outcome == Outcome.Reset ? 1 : 0;
        }

        var staffOutcome = await EnsureAccountAsync(StaffEmail, "Test Skladník", AccountRoles.Warehouse, password, errors);
        created += staffOutcome == Outcome.Created ? 1 : 0;
        reset += staffOutcome == Outcome.Reset ? 1 : 0;
        var staff = await userManager.FindByEmailAsync(StaffEmail);
        if (staff is null)
        {
            return new(emails, StaffEmail, string.Empty, created, errors) { Reset = reset };
        }

        // 2FA: stejné kroky jako na stránce /Account/TwoFactor, jen bez telefonu – klíč si generátor spočítá sám.
        if (await userManager.GetAuthenticatorKeyAsync(staff) is null)
        {
            await userManager.ResetAuthenticatorKeyAsync(staff);
        }

        await userManager.SetTwoFactorEnabledAsync(staff, true);
        var key = await userManager.GetAuthenticatorKeyAsync(staff) ?? string.Empty;

        logger.LogInformation("Testovací účty generátoru provozu: nově {Created}, nové heslo {Reset}", created, reset);
        return new(emails, StaffEmail, key, created, errors) { Reset = reset };
    }

    private enum Outcome { Created, Reset, Failed }

    private async Task<Outcome> EnsureAccountAsync(string email, string displayName, string role, string password, List<string> errors)
    {
        var user = await userManager.FindByEmailAsync(email);
        IdentityResult result;
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName };
            result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                result = await userManager.AddToRoleAsync(user, role);
            }

            return Record(result, Outcome.Created);
        }

        // Existující testovací účet: stejné heslo jako ostatní (heslo je ověřené předem, takže účet bez hesla nezůstane)
        // a bez blokace – zablokovaný účet by se v generátoru tvářil jako špatné heslo.
        result = await userManager.HasPasswordAsync(user) ? await userManager.RemovePasswordAsync(user) : IdentityResult.Success;
        if (result.Succeeded)
        {
            result = await userManager.AddPasswordAsync(user, password);
        }

        if (result.Succeeded)
        {
            result = await userManager.SetLockoutEndDateAsync(user, null);
        }

        if (result.Succeeded)
        {
            result = await userManager.ResetAccessFailedCountAsync(user);
        }

        return Record(result, Outcome.Reset);

        Outcome Record(IdentityResult identityResult, Outcome success)
        {
            if (identityResult.Succeeded)
            {
                return success;
            }

            errors.AddRange(identityResult.Errors.Select(error => $"{email}: {error.Description}"));
            return Outcome.Failed;
        }
    }

    private async Task<List<string>> ValidatePasswordAsync(string password)
    {
        var probe = new ApplicationUser { UserName = StaffEmail, Email = StaffEmail };
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, probe, password);
            errors.AddRange(result.Errors.Select(error => error.Description));
        }

        return errors;
    }
}
