using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Audit přihlášení (2FA krok B): zápis pokusů, výpis a úklid. <see cref="RecordAsync"/> <b>nikdy nevyhodí výjimku</b> –
/// chyba auditu nesmí rozbít přihlášení, jen se zapíše do logu.
/// </summary>
public sealed class LoginAttemptRecorder(
    AccountsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<LoginAttemptRecorder> logger)
{
    /// <summary>Jak dlouho se pokusy drží (IP je osobní údaj).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    public async Task RecordAsync(string? userId, LoginMethod method, LoginResult result, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            dbContext.LoginAttempts.Add(new LoginAttempt(userId, method, result, timeProvider.GetUtcNow().UtcDateTime, ipAddress));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Do logu jen typ chyby – žádný e-mail ani heslo.
            logger.LogWarning("Audit přihlášení se nepodařilo zapsat: {Error}", ex.GetType().Name);
        }
    }

    /// <summary>Posledních <paramref name="take"/> pokusů účtu, nejnovější první.</summary>
    public async Task<IReadOnlyList<LoginAttemptItem>> GetRecentAsync(string userId, int take,
        CancellationToken cancellationToken = default) =>
        await dbContext.LoginAttempts.AsNoTracking()
            .Where(attempt => attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.Id)
            .Take(take)
            .Select(attempt => new LoginAttemptItem(attempt.OccurredAtUtc, attempt.Method, attempt.Result, attempt.IpAddress))
            .ToListAsync(cancellationToken);

    /// <summary>Smaže pokusy starší než <see cref="Retention"/>; vrací počet smazaných.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - Retention;
        return await dbContext.LoginAttempts
            .Where(attempt => attempt.OccurredAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
