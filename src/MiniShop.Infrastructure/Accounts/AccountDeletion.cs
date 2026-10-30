using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Účty krok 9: uživatel si smaže účet (GDPR – právo být zapomenut). Smaže se řádek v AspNetUsers i s rolemi
/// a claimy (cascade), z auditu správy uživatelů zmizí jeho e-mail.
/// </summary>
/// <remarks>
/// Objednávky (jiný modul, jiná databáze) anonymizuje Web <b>před</b> voláním <see cref="DeleteAsync"/> – když smazání
/// selže, zkusí to uživatel znovu a anonymizace proběhne podruhé bez škody. Obráceně by mohl zůstat smazaný účet
/// s objednávkami, na kterých je pořád jeho jméno a e-mail.
/// </remarks>
public sealed class AccountDeletion(
    AccountsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ILogger<AccountDeletion> logger)
{
    /// <summary>Smí si uživatel účet smazat? Null = ano, jinak důvod. Admin ne – nejdřív ho jiný admin vrátí na usera.</summary>
    public async Task<string?> CanDeleteAsync(ApplicationUser user) =>
        await userManager.IsInRoleAsync(user, AccountRoles.Admin)
            ? "Admin si účet smazat nemůže – nejdřív vás musí jiný admin vrátit na usera."
            : null;

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (await CanDeleteAsync(user) is { } reason)
        {
            return IdentityResult.Failed(new IdentityError { Code = "CannotDelete", Description = reason });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var auditEntries = await dbContext.AuditEntries
            .Where(entry => entry.ActorUserId == user.Id || entry.TargetUserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var entry in auditEntries)
        {
            entry.ForgetUser(user.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return result;   // bez Commit se transakce vrátí – audit zůstane, jak byl
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Uživatel {UserId} smazal svůj účet", user.Id);
        return result;
    }
}
