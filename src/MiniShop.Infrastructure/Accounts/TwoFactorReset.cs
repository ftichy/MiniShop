using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Výsledek <see cref="TwoFactorReset.ResetAsync"/> – text je pro člověka u terminálu.</summary>
public sealed record TwoFactorResetResult(bool Succeeded, string Message);

/// <summary>
/// 2FA krok A2: nouzová cesta, když admin ztratí telefon i záchranné kódy. Spouští se jen na serveru
/// (<c>dotnet MiniShop.Web.dll reset-2fa --email …</c>) – kdo se dostane do kontejneru, má stejnou důvěru jako u
/// <c>create-admin</c>. Dvoufázové ověření se vypne, tajný klíč i záchranné kódy se zahodí, nový security stamp
/// odhlásí všechna přihlášení účtu a akce se zapíše do auditu. Admin si při dalším přihlášení musí 2FA nastavit znovu.
/// </summary>
public sealed class TwoFactorReset(
    UserManager<ApplicationUser> userManager,
    AccountsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<TwoFactorReset> logger)
{
    public async Task<TwoFactorResetResult> ResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            return new(false, "Účet s tímto e-mailem neexistuje.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded)
        {
            return new(false, string.Join(" ", disabled.Errors.Select(error => error.Description)));
        }

        // Starý tajný klíč přestane platit (pro další zapnutí se vytvoří nový) …
        var reset = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
        {
            return new(false, string.Join(" ", reset.Errors.Select(error => error.Description)));
        }

        // … a záchranné kódy se nahradí prázdnou sadou = všechny staré přestanou platit.
        await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);

        var stamp = await userManager.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded)
        {
            return new(false, string.Join(" ", stamp.Errors.Select(error => error.Description)));
        }

        dbContext.AuditEntries.Add(AccountAuditEntry.ByServerCommand(
            user, AccountAuditAction.TwoFactorReset, timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogWarning("Dvoufázové ověření účtu {UserId} bylo zrušeno příkazem na serveru", user.Id);
        return new(true, $"Dvoufázové ověření účtu {user.Email} je zrušené. Při dalším přihlášení si ho nastaví znovu.");
    }
}
