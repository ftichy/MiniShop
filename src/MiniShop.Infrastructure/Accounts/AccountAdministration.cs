using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>Účet v přehledu pro správce uživatelů (účty krok 6).</summary>
/// <param name="Roles">Účty krok 12A: přidělené role v pořadí <see cref="AccountRoles.All"/> (bez původní role Admin).</param>
/// <param name="IsBlocked">Zablokovaný správcem (do odblokování).</param>
/// <param name="LockedOutUntil">Účty krok 8: dočasně zamčený po špatných heslech – do kdy (jinak null).</param>
public sealed record AccountSummary(
    string Id,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles,
    bool EmailConfirmed,
    bool IsBlocked,
    DateTimeOffset? LockedOutUntil)
{
    /// <summary>Má aspoň jednu zaměstnaneckou roli (vyžaduje 2FA, nesmí si smazat účet).</summary>
    public bool IsStaff => Roles.Any(AccountRoles.IsStaff);

    public bool IsCustomer => Roles.Contains(AccountRoles.User);
}

/// <summary>Řádek auditu pro zobrazení.</summary>
public sealed record AccountAuditItem(
    DateTime OccurredAtUtc,
    string ActorEmail,
    string TargetUserId,
    string TargetEmail,
    AccountAuditAction Action,
    string? Detail = null);

/// <summary>Účty krok 12A: co se na rolích změnilo – podklad pro e-mail dotyčnému.</summary>
public sealed record AccountRoleChange(
    string Email,
    string DisplayName,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed)
{
    /// <summary>Změnila se zaměstnanecká role (oprávnění ve správě) – o tom dostane uživatel e-mail.</summary>
    public bool ChangesStaffAccess => Added.Concat(Removed).Any(AccountRoles.IsStaff);

    /// <summary>„+Sklad a expedice, −Marketing“ – do auditu a e-mailu.</summary>
    public string Describe() => string.Join(", ",
        Added.Select(role => "+" + AccountRoles.Label(role)).Concat(Removed.Select(role => "−" + AccountRoles.Label(role))));
}

/// <summary>Výsledek akce správce – text se ukáže na stránce.</summary>
/// <param name="RoleChange">Účty krok 12A: co se změnilo na rolích (pro e-mail dotyčnému), jinak null.</param>
public sealed record AccountAdminResult(bool Succeeded, string Message, AccountRoleChange? RoleChange = null)
{
    public static AccountAdminResult Ok(string message, AccountRoleChange? roleChange = null) => new(true, message, roleChange);
    public static AccountAdminResult Failed(string message) => new(false, message);
}

/// <summary>
/// Účty krok 6: správa uživatelů – role, zablokování a odblokování. Účty krok 12A: uživatel má libovolnou sadu rolí
/// (<see cref="AccountRoles.All"/>), mění ji jen správce uživatelů (<see cref="AccountRoles.UserAdmin"/>).
/// </summary>
/// <remarks>
/// Pravidla:
/// <list type="bullet">
/// <item>Akci smí provést jen aktivní (nezablokovaný) správce uživatelů – ověřuje se v databázi, ne jen v cookie.</item>
/// <item>Vždy musí zůstat aspoň jeden aktivní správce uživatelů – jinak by nikdo nemohl přidělovat role.</item>
/// <item>Správce si nemůže odebrat roli správce uživatelů ani zablokovat sám sebe (aby se omylem nezamkl).
/// Jiné role si měnit smí – třeba si přidat roli Zákazník.</item>
/// </list>
/// Kontrola i změna běží v jedné transakci pod zámkem (pg_advisory_xact_lock) – dvě souběžné změny (dva správci
/// odebírají jeden druhého) proběhnou postupně a druhá už vidí výsledek první. Každá změna vymění security stamp: staré
/// přihlášení (cookie) dotyčného do minuty přestane platit (SecurityStampValidator ve Webu) a zapíše se auditní záznam.
/// </remarks>
public sealed class AccountAdministration(
    AccountsDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<AccountAdministration> logger)
{
    public async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users.AsNoTracking().OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var roles = await RolesByUserAsync(users.Select(user => user.Id).ToList(), cancellationToken);
        return users.Select(user => ToSummary(user, roles)).ToList();
    }

    /// <summary>
    /// Stránka účtů seřazená podle e-mailu; <paramref name="emailSearch"/> = část e-mailu (bez ohledu na velikost písmen,
    /// hledá se v normalizovaném e-mailu – index Identity).
    /// </summary>
    public async Task<PagedList<AccountSummary>> GetAccountsPageAsync(string? emailSearch, PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var search = string.IsNullOrWhiteSpace(emailSearch) ? null : emailSearch.Trim().ToUpperInvariant();
        var users = await dbContext.Users.AsNoTracking()
            .Where(user => search == null || user.NormalizedEmail!.Contains(search))
            .OrderBy(user => user.Email)
            .ToPagedListAsync(page, cancellationToken);
        var roles = await RolesByUserAsync(users.Items.Select(user => user.Id).ToList(), cancellationToken);
        return users.Map(user => ToSummary(user, roles));
    }

    public async Task<AccountSummary?> GetAccountAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : ToSummary(user, await RolesByUserAsync([id], cancellationToken));
    }

    /// <summary>Historie účtu (co s ním správci dělali), nebo celý audit (<paramref name="targetUserId"/> = null).</summary>
    public async Task<IReadOnlyList<AccountAuditItem>> GetAuditAsync(string? targetUserId, int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.AuditEntries.AsNoTracking()
            .Where(entry => targetUserId == null || entry.TargetUserId == targetUserId)
            .OrderByDescending(entry => entry.Id)
            .Take(take)
            .Select(entry => new AccountAuditItem(
                entry.OccurredAtUtc, entry.ActorEmail, entry.TargetUserId, entry.TargetEmail, entry.Action, entry.Detail))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Účty krok 12A: nastaví uživateli přesně tuto sadu rolí (zaškrtnuté na stránce). Původní role Admin se při tom odebere –
    /// jeho práva už mají zaměstnanecké role.
    /// </summary>
    public async Task<AccountAdminResult> SetRolesAsync(string actorId, string targetId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        var unknown = roles.FirstOrDefault(role => !AccountRoles.All.Contains(role));
        if (unknown is not null)
        {
            return AccountAdminResult.Failed($"Neznámá role {unknown}.");
        }

        var wanted = AccountRoles.All.Where(roles.Contains).ToList();
        return await RunAsync(actorId, targetId, async (actor, target) =>
        {
            var current = await userManager.GetRolesAsync(target);
            var added = wanted.Where(role => !current.Contains(role)).ToList();
            var removed = AccountRoles.All.Where(role => current.Contains(role) && !wanted.Contains(role)).ToList();
            var legacy = current.Contains(AccountRoles.LegacyAdmin);
            if (added.Count == 0 && removed.Count == 0 && !legacy)
            {
                return (AccountAdminResult.Failed("Role se nezměnily."), null);
            }

            if (removed.Contains(AccountRoles.UserAdmin))
            {
                if (actor.Id == target.Id)
                {
                    return (AccountAdminResult.Failed("Roli správce uživatelů si nemůžete odebrat sám – požádejte jiného správce."), null);
                }

                if (!await AnotherActiveUserAdminExistsAsync(target))
                {
                    return (AccountAdminResult.Failed("Posledního správce uživatelů nejde odebrat."), null);
                }
            }

            var result = IdentityResult.Success;
            var toRemove = removed.Concat(legacy ? new[] { AccountRoles.LegacyAdmin } : Array.Empty<string>()).ToList();
            if (toRemove.Count > 0)
            {
                result = await userManager.RemoveFromRolesAsync(target, toRemove);
            }

            if (result.Succeeded && added.Count > 0)
            {
                result = await userManager.AddToRolesAsync(target, added);
            }

            var change = new AccountRoleChange(target.Email ?? string.Empty, target.DisplayName, added, removed);
            return result.Succeeded
                ? (AccountAdminResult.Ok($"Role účtu {target.Email} uložené.", change), change.Describe())
                : (AccountAdminResult.Failed(string.Join(" ", result.Errors.Select(e => e.Description))), null);
        }, AccountAuditAction.RolesChanged, cancellationToken);
    }

    public Task<AccountAdminResult> LockAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        RunAsync(actorId, targetId, async (actor, target) =>
        {
            if (IsBlocked(target)) return (AccountAdminResult.Failed("Účet už je zablokovaný."), null);
            if (await IsActiveUserAdminAsync(target) && !await AnotherActiveUserAdminExistsAsync(target))
            {
                return (AccountAdminResult.Failed("Posledního správce uživatelů nejde zablokovat."), null);
            }

            if (actor.Id == target.Id) return (AccountAdminResult.Failed("Sám sebe zablokovat nemůžete."), null);

            var result = await userManager.SetLockoutEnabledAsync(target, true);
            if (result.Succeeded)
            {
                result = await userManager.SetLockoutEndDateAsync(target, DateTimeOffset.MaxValue);
            }

            return (ToResult(result, $"Účet {target.Email} je zablokovaný."), null);
        }, AccountAuditAction.Locked, cancellationToken);

    public Task<AccountAdminResult> UnlockAsync(string actorId, string targetId, CancellationToken cancellationToken = default) =>
        RunAsync(actorId, targetId, async (_, target) =>
        {
            // Odblokovat jde i dočasný zámek po špatných heslech (účty krok 8).
            if (!await userManager.IsLockedOutAsync(target))
            {
                return (AccountAdminResult.Failed("Účet není zablokovaný ani zamčený."), null);
            }

            var result = await userManager.SetLockoutEndDateAsync(target, null);
            if (result.Succeeded)
            {
                result = await userManager.ResetAccessFailedCountAsync(target);
            }

            return (ToResult(result, $"Účet {target.Email} je odblokovaný."), null);
        }, AccountAuditAction.Unlocked, cancellationToken);

    /// <summary>
    /// Společný rámec akce: transakce + zámek, kontrola správce, změna, nový security stamp, audit. Neúspěch = rollback.
    /// </summary>
    private async Task<AccountAdminResult> RunAsync(string actorId, string targetId,
        Func<ApplicationUser, ApplicationUser, Task<(AccountAdminResult Result, string? AuditDetail)>> change,
        AccountAuditAction action, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Škálování krok 3C: změny správy uživatelů jedna po druhé. PostgreSQL pouští transakce souběžně – dva správci,
        // kteří si naráz navzájem odebírají roli, by oba prošli kontrolou „zůstane jiný správce“. Zámek drží transakce
        // (uvolní se při commitu/rollbacku); druhá počká a pak už vidí změnu té první.
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('minishop.account-administration'))", cancellationToken);

        var actor = await userManager.FindByIdAsync(actorId);
        if (actor is null || !await IsActiveUserAdminAsync(actor))
        {
            return AccountAdminResult.Failed("Uživatele smí spravovat jen aktivní správce uživatelů.");
        }

        var target = await userManager.FindByIdAsync(targetId);
        if (target is null)
        {
            return AccountAdminResult.Failed("Uživatel neexistuje.");
        }

        var (result, detail) = await change(actor, target);
        if (!result.Succeeded)
        {
            return result;   // bez Commit se transakce při Dispose vrátí – nic se nezmění napůl
        }

        // Nový security stamp = dosavadní přihlášení dotyčného přestanou platit (nové role až po novém ověření cookie).
        var stamp = await userManager.UpdateSecurityStampAsync(target);
        if (!stamp.Succeeded)
        {
            return ToResult(stamp, string.Empty);
        }

        dbContext.AuditEntries.Add(new AccountAuditEntry(actor, target, action, timeProvider.GetUtcNow().UtcDateTime, detail));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Správce {ActorId} provedl {Action} u účtu {TargetId} {Detail}", actor.Id, action, target.Id, detail);
        return result;
    }

    private static AccountAdminResult ToResult(IdentityResult result, string message) => result.Succeeded
        ? AccountAdminResult.Ok(message)
        : AccountAdminResult.Failed(string.Join(" ", result.Errors.Select(e => e.Description)));

    /// <summary>
    /// Zablokovaný správcem = zámek do <see cref="DateTimeOffset.MaxValue"/>. Účty krok 8: dočasný zámek po špatných
    /// heslech (15 minut) používá stejné pole LockoutEnd, ale správce o práva nepřipraví – jinak by útočník mohl
    /// zkoušením hesel „odstavit“ všechny správce.
    /// </summary>
    private static bool IsBlocked(ApplicationUser user) => user.LockoutEnd?.Year == DateTimeOffset.MaxValue.Year;

    private async Task<bool> IsActiveUserAdminAsync(ApplicationUser user) =>
        await userManager.IsInRoleAsync(user, AccountRoles.UserAdmin) && !IsBlocked(user);

    private async Task<bool> AnotherActiveUserAdminExistsAsync(ApplicationUser except)
    {
        foreach (var admin in await userManager.GetUsersInRoleAsync(AccountRoles.UserAdmin))
        {
            if (admin.Id != except.Id && !IsBlocked(admin))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Role vybraných účtů jedním dotazem (stránka přehledu).</summary>
    private async Task<ILookup<string, string>> RolesByUserAsync(IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken) =>
        (await (from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken))
        .ToLookup(row => row.UserId, row => row.Name!);

    /// <summary>Zámek se počítá v paměti (z doby SQLite, které neumělo v dotazu porovnávat DateTimeOffset; uživatelů je málo).</summary>
    private AccountSummary ToSummary(ApplicationUser user, ILookup<string, string> roles)
    {
        var blocked = IsBlocked(user);
        var lockedOutUntil = !blocked && user.LockoutEnabled && user.LockoutEnd > timeProvider.GetUtcNow()
            ? user.LockoutEnd
            : null;
        var userRoles = AccountRoles.All.Where(role => roles[user.Id].Contains(role)).ToList();
        return new(user.Id, user.DisplayName, user.Email ?? string.Empty, userRoles, user.EmailConfirmed, blocked, lockedOutUntil);
    }
}
