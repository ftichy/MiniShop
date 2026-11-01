using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Účet v přehledu pro admina (účty krok 6).</summary>
/// <param name="IsBlocked">Zablokovaný adminem (do odblokování).</param>
/// <param name="LockedOutUntil">Účty krok 8: dočasně zamčený po špatných heslech – do kdy (jinak null).</param>
public sealed record AccountSummary(
    string Id,
    string DisplayName,
    string Email,
    bool IsAdmin,
    bool EmailConfirmed,
    bool IsBlocked,
    DateTimeOffset? LockedOutUntil);

/// <summary>Řádek auditu pro zobrazení.</summary>
public sealed record AccountAuditItem(
    DateTime OccurredAtUtc,
    string ActorEmail,
    string TargetUserId,
    string TargetEmail,
    AccountAuditAction Action);

/// <summary>Výsledek akce admina – text se ukáže na stránce.</summary>
public sealed record AccountAdminResult(bool Succeeded, string Message)
{
    public static AccountAdminResult Ok(string message) => new(true, message);
    public static AccountAdminResult Failed(string message) => new(false, message);
}

/// <summary>
/// Účty krok 6: správa uživatelů adminem – povýšení na admina, vrácení na usera, zablokování a odblokování.
/// </summary>
/// <remarks>
/// Pravidla:
/// <list type="bullet">
/// <item>Akci smí provést jen aktivní (nezablokovaný) admin – ověřuje se v databázi, ne jen v cookie.</item>
/// <item>Vždy musí zůstat aspoň jeden aktivní admin – jinak by obchod neměl kdo spravovat.</item>
/// <item>Admin nemůže odebrat roli ani zablokovat sám sebe (aby se omylem nezamkl).</item>
/// <item>Role jsou výhradní: User ↔ Admin (admin neobjednává).</item>
/// </list>
/// Kontrola i změna běží v jedné transakci – dvě souběžné změny (dva admini odebírají jeden druhého) SQLite
/// nepustí najednou, druhá skončí chybou. Každá změna vymění security stamp: staré přihlášení (cookie)
/// dotyčného do minuty přestane platit (SecurityStampValidator ve Webu) a zapíše se auditní záznam.
/// </remarks>
public sealed class AccountAdministration(
    AccountsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<AccountAdministration> logger)
{
    public async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var adminIds = await AdminIdsQuery().ToListAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking().OrderBy(user => user.Email).ToListAsync(cancellationToken);
        return users.Select(user => ToSummary(user, adminIds.Contains(user.Id))).ToList();
    }

    public async Task<AccountSummary?> GetAccountAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : ToSummary(user, await AdminIdsQuery().ContainsAsync(id, cancellationToken));
    }

    /// <summary>Historie účtu (co s ním admini dělali), nebo celý audit (<paramref name="targetUserId"/> = null).</summary>
    public async Task<IReadOnlyList<AccountAuditItem>> GetAuditAsync(string? targetUserId, int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.AuditEntries.AsNoTracking()
            .Where(entry => targetUserId == null || entry.TargetUserId == targetUserId)
            .OrderByDescending(entry => entry.Id)
            .Take(take)
            .Select(entry => new AccountAuditItem(
                entry.OccurredAtUtc, entry.ActorEmail, entry.TargetUserId, entry.TargetEmail, entry.Action))
            .ToListAsync(cancellationToken);

    public Task<AccountAdminResult> PromoteToAdminAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, targetId, AccountAuditAction.PromotedToAdmin, cancellationToken);

    public Task<AccountAdminResult> DemoteToUserAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, targetId, AccountAuditAction.DemotedToUser, cancellationToken);

    public Task<AccountAdminResult> LockAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, targetId, AccountAuditAction.Locked, cancellationToken);

    public Task<AccountAdminResult> UnlockAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        ChangeAsync(actorId, targetId, AccountAuditAction.Unlocked, cancellationToken);

    private async Task<AccountAdminResult> ChangeAsync(string actorId, string targetId, AccountAuditAction action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var actor = await userManager.FindByIdAsync(actorId);
        if (actor is null || !await IsActiveAdminAsync(actor))
        {
            return AccountAdminResult.Failed("Uživatele smí spravovat jen aktivní admin.");
        }

        var target = await userManager.FindByIdAsync(targetId);
        if (target is null)
        {
            return AccountAdminResult.Failed("Uživatel neexistuje.");
        }

        var error = await ValidateAsync(actor, target, action);
        if (error is not null)
        {
            return AccountAdminResult.Failed(error);
        }

        var result = action switch
        {
            AccountAuditAction.PromotedToAdmin => await ReplaceRoleAsync(target, AccountRoles.User, AccountRoles.Admin),
            AccountAuditAction.DemotedToUser => await ReplaceRoleAsync(target, AccountRoles.Admin, AccountRoles.User),
            AccountAuditAction.Locked => await LockOutAsync(target),
            AccountAuditAction.Unlocked => await UnlockOutAsync(target),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        if (result.Succeeded)
        {
            // Nový security stamp = dosavadní přihlášení dotyčného přestanou platit (nová role až po novém přihlášení).
            result = await userManager.UpdateSecurityStampAsync(target);
        }

        if (!result.Succeeded)
        {
            // Bez Commit se transakce při Dispose vrátí – nic se nezmění napůl.
            return AccountAdminResult.Failed(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        dbContext.AuditEntries.Add(new AccountAuditEntry(actor, target, action, timeProvider.GetUtcNow().UtcDateTime));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Admin {ActorId} provedl {Action} u účtu {TargetId}", actor.Id, action, target.Id);
        return AccountAdminResult.Ok(SuccessMessage(action, target));
    }

    private async Task<string?> ValidateAsync(ApplicationUser actor, ApplicationUser target, AccountAuditAction action)
    {
        var targetIsAdmin = await userManager.IsInRoleAsync(target, AccountRoles.Admin);
        var isSelf = actor.Id == target.Id;

        switch (action)
        {
            case AccountAuditAction.PromotedToAdmin:
                return targetIsAdmin ? "Uživatel už je admin." : null;

            case AccountAuditAction.DemotedToUser:
                if (!targetIsAdmin) return "Uživatel není admin.";
                if (!await AnotherActiveAdminExistsAsync(target)) return "Posledního admina nejde vrátit na usera.";
                return isSelf ? "Roli admina si nemůžete odebrat sám – požádejte jiného admina." : null;

            case AccountAuditAction.Locked:
                if (IsBlocked(target)) return "Účet už je zablokovaný.";
                if (targetIsAdmin && !await AnotherActiveAdminExistsAsync(target)) return "Posledního admina nejde zablokovat.";
                return isSelf ? "Sám sebe zablokovat nemůžete." : null;

            case AccountAuditAction.Unlocked:
                // Odblokovat jde i dočasný zámek po špatných heslech (účty krok 8).
                return await userManager.IsLockedOutAsync(target) ? null : "Účet není zablokovaný ani zamčený.";

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    /// <summary>
    /// Zablokovaný adminem = zámek do <see cref="DateTimeOffset.MaxValue"/>. Účty krok 8: dočasný zámek po špatných
    /// heslech (15 minut) používá stejné pole LockoutEnd, ale admina o práva nepřipraví – jinak by útočník mohl
    /// zkoušením hesel „odstavit“ všechny adminy.
    /// </summary>
    private static bool IsBlocked(ApplicationUser user) => user.LockoutEnd?.Year == DateTimeOffset.MaxValue.Year;

    private async Task<bool> IsActiveAdminAsync(ApplicationUser user) =>
        await userManager.IsInRoleAsync(user, AccountRoles.Admin) && !IsBlocked(user);

    private async Task<bool> AnotherActiveAdminExistsAsync(ApplicationUser except)
    {
        foreach (var admin in await userManager.GetUsersInRoleAsync(AccountRoles.Admin))
        {
            if (admin.Id != except.Id && !IsBlocked(admin))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IdentityResult> ReplaceRoleAsync(ApplicationUser user, string from, string to)
    {
        if (await userManager.IsInRoleAsync(user, from))
        {
            var removed = await userManager.RemoveFromRoleAsync(user, from);
            if (!removed.Succeeded)
            {
                return removed;
            }
        }

        return await userManager.AddToRoleAsync(user, to);
    }

    /// <summary>Zablokování = zámek účtu „navždy“ (do odblokování). Přihlášení pak skončí jako neúspěšné.</summary>
    private async Task<IdentityResult> LockOutAsync(ApplicationUser user)
    {
        var result = await userManager.SetLockoutEnabledAsync(user, true);
        return result.Succeeded ? await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue) : result;
    }

    private async Task<IdentityResult> UnlockOutAsync(ApplicationUser user)
    {
        var result = await userManager.SetLockoutEndDateAsync(user, null);
        return result.Succeeded ? await userManager.ResetAccessFailedCountAsync(user) : result;
    }

    private IQueryable<string> AdminIdsQuery() =>
        from userRole in dbContext.UserRoles
        join role in dbContext.Roles on userRole.RoleId equals role.Id
        where role.Name == AccountRoles.Admin
        select userRole.UserId;

    /// <summary>Zámek se počítá v paměti – SQLite neumí v dotazu porovnávat DateTimeOffset (LockoutEnd).</summary>
    private AccountSummary ToSummary(ApplicationUser user, bool isAdmin)
    {
        var blocked = IsBlocked(user);
        var lockedOutUntil = !blocked && user.LockoutEnabled && user.LockoutEnd > timeProvider.GetUtcNow()
            ? user.LockoutEnd
            : null;
        return new(user.Id, user.DisplayName, user.Email ?? string.Empty, isAdmin, user.EmailConfirmed, blocked, lockedOutUntil);
    }

    private static string SuccessMessage(AccountAuditAction action, ApplicationUser target) => action switch
    {
        AccountAuditAction.PromotedToAdmin => $"{target.Email} je teď admin.",
        AccountAuditAction.DemotedToUser => $"{target.Email} je teď user.",
        AccountAuditAction.Locked => $"Účet {target.Email} je zablokovaný.",
        AccountAuditAction.Unlocked => $"Účet {target.Email} je odblokovaný.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
