namespace MiniShop.Infrastructure.Accounts;

/// <summary>Co admin s účtem udělal (účty krok 6). Ukládá se jako text – nové akce jen přidávat.</summary>
public enum AccountAuditAction
{
    PromotedToAdmin,
    DemotedToUser,
    Locked,
    Unlocked,

    /// <summary>2FA krok A2: příkaz na serveru (<c>reset-2fa</c>) zrušil dvoufázové ověření – ztráta telefonu.</summary>
    TwoFactorReset
}

/// <summary>
/// Auditní záznam správy účtů (účty krok 6): kdo, komu, co a kdy. Jen INSERT – záznamy se nemění ani nemažou.
/// </summary>
/// <remarks>
/// E-maily jsou kopie z doby akce – záznam zůstane čitelný, i když si uživatel e-mail později změní.
/// </remarks>
public sealed class AccountAuditEntry
{
    public const int UserIdMaxLength = 64;
    public const int EmailMaxLength = 256;

    private AccountAuditEntry()
    {
    }

    public AccountAuditEntry(ApplicationUser actor, ApplicationUser target, AccountAuditAction action, DateTime occurredAtUtc)
    {
        ActorUserId = actor.Id;
        ActorEmail = actor.Email ?? string.Empty;
        TargetUserId = target.Id;
        TargetEmail = target.Email ?? string.Empty;
        Action = action;
        OccurredAtUtc = occurredAtUtc;
    }

    /// <summary>Akci neprovedl admin v aplikaci, ale příkaz na serveru (<c>reset-2fa</c>) – kdo ho spustil, ví jen přístup do kontejneru.</summary>
    public static AccountAuditEntry ByServerCommand(ApplicationUser target, AccountAuditAction action, DateTime occurredAtUtc) =>
        new()
        {
            ActorUserId = ServerCommandActor,
            ActorEmail = ServerCommandActor,
            TargetUserId = target.Id,
            TargetEmail = target.Email ?? string.Empty,
            Action = action,
            OccurredAtUtc = occurredAtUtc
        };

    public const string ServerCommandActor = "příkaz na serveru";

    public long Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public string ActorEmail { get; private set; } = string.Empty;
    public string TargetUserId { get; private set; } = string.Empty;
    public string TargetEmail { get; private set; } = string.Empty;
    public AccountAuditAction Action { get; private set; }

    /// <summary>Místo e-mailu smazaného účtu (účty krok 9).</summary>
    public const string DeletedAccountEmail = "smazaný účet";

    /// <summary>
    /// Účty krok 9: uživatel smazal účet – v auditu zůstane, že se akce stala (kdo jako ID), ale bez jeho e-mailu.
    /// Jediná výjimka z „audit se jen přidává“: právo být zapomenut je silnější.
    /// </summary>
    public void ForgetUser(string userId)
    {
        if (ActorUserId == userId)
        {
            ActorEmail = DeletedAccountEmail;
        }

        if (TargetUserId == userId)
        {
            TargetEmail = DeletedAccountEmail;
        }
    }
}
