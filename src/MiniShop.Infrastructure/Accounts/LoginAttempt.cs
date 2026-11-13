namespace MiniShop.Infrastructure.Accounts;

/// <summary>Čím se kdo přihlašoval (audit přihlášení, 2FA krok B). Ukládá se jako text – nové hodnoty jen na konec.</summary>
public enum LoginMethod
{
    Password,
    AuthenticatorCode,
    RecoveryCode,
    Google
}

/// <summary>Jak pokus dopadl. Ukládá se jako text – nové hodnoty jen na konec.</summary>
public enum LoginResult
{
    Succeeded,
    WrongPassword,
    WrongCode,

    /// <summary>Dočasný zámek po špatných pokusech.</summary>
    LockedOut,

    /// <summary>Zablokováno adminem.</summary>
    Blocked,

    /// <summary>Google nepotvrdil e-mail, nebo existující účet nemá potvrzený e-mail.</summary>
    UnconfirmedEmail,

    /// <summary>Jiná chyba přihlášení přes Google (zrušeno, vypršelo, propojení selhalo).</summary>
    Failed,

    /// <summary>První krok prošel (heslo, Google), teď se čeká na kód z aplikace.</summary>
    AwaitingSecondFactor
}

/// <summary>
/// Audit přihlášení (2FA krok B): jeden pokus o přihlášení – kdo (ID účtu, u neznámého e-mailu null), čím, jak dopadl,
/// kdy a z které IP. <b>E-mail ani heslo se neukládají.</b> IP adresa je osobní údaj – záznamy se po
/// <see cref="LoginAttemptRecorder.Retention"/> mažou a se smazáním účtu zmizí hned.
/// </summary>
public sealed class LoginAttempt
{
    public const int IpMaxLength = 45;   // IPv6 text

    private LoginAttempt()
    {
    }

    public LoginAttempt(string? userId, LoginMethod method, LoginResult result, DateTime occurredAtUtc, string? ipAddress)
    {
        UserId = userId;
        Method = method;
        Result = result;
        OccurredAtUtc = occurredAtUtc;
        IpAddress = ipAddress is { Length: > IpMaxLength } ? ipAddress[..IpMaxLength] : ipAddress;
    }

    public long Id { get; private set; }

    /// <summary>Účet, ke kterému pokus patřil; null = neznámý e-mail nebo Google bez účtu. Bez cizího klíče – smazání účtu maže záznamy samo.</summary>
    public string? UserId { get; private set; }

    public LoginMethod Method { get; private set; }
    public LoginResult Result { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? IpAddress { get; private set; }
}

/// <summary>Řádek výpisu pokusů o přihlášení (detail uživatele, stránka účtu).</summary>
public sealed record LoginAttemptItem(DateTime OccurredAtUtc, LoginMethod Method, LoginResult Result, string? IpAddress);
