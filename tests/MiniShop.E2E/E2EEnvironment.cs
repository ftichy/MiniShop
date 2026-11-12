namespace MiniShop.E2E;

/// <summary>
/// Nastavení E2E testů z proměnných prostředí. Bez <c>E2E_BASE_URL</c> se testy přeskočí
/// (<c>dotnet test MiniShop.slnx</c> bez spuštěné aplikace tedy nezčervená).
/// </summary>
public static class E2EEnvironment
{
    /// <summary>Adresa Webu, např. http://localhost:8080 (musí být přesně „localhost“ – cookie je Secure, jen tam ji Chromium na HTTP pustí).</summary>
    public static string? BaseUrl => Normalize(Environment.GetEnvironmentVariable("E2E_BASE_URL"));

    /// <summary>Webové rozhraní/API Mailpitu, odkud se čtou potvrzovací e-maily.</summary>
    public static string MailpitUrl =>
        Normalize(Environment.GetEnvironmentVariable("E2E_MAILPIT_URL")) ?? "http://localhost:8025";

    /// <summary>Admin založený před testy příkazem create-admin (heslo vygeneruje CI za běhu – v gitu není).</summary>
    public static string? AdminEmail => Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL");

    public static string? AdminPassword => Environment.GetEnvironmentVariable("E2E_ADMIN_PASSWORD");

    /// <summary>Kam se při selhání uloží Playwright trace a snímek (CI je nahraje jako artefakt).</summary>
    public static string ArtifactsDir =>
        Environment.GetEnvironmentVariable("E2E_ARTIFACTS_DIR") ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts");

    /// <summary>Čekání na asynchronní řetěz (outbox → RabbitMQ → sága trvá pár sekund).</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(40);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
}

/// <summary>Jako [Fact], ale bez E2E_BASE_URL se test přeskočí.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (E2EEnvironment.BaseUrl is null)
        {
            Skip = "E2E: není nastavené E2E_BASE_URL (aplikace neběží) – viz docs/prirucka/github/ci.md.";
        }
    }
}

/// <summary>Pro scénáře, které potřebují admina (E2E_ADMIN_EMAIL + E2E_ADMIN_PASSWORD).</summary>
public sealed class E2EAdminFactAttribute : FactAttribute
{
    public E2EAdminFactAttribute()
    {
        if (E2EEnvironment.BaseUrl is null)
        {
            Skip = "E2E: není nastavené E2E_BASE_URL (aplikace neběží) – viz docs/prirucka/github/ci.md.";
        }
        else if (string.IsNullOrEmpty(E2EEnvironment.AdminEmail) || string.IsNullOrEmpty(E2EEnvironment.AdminPassword))
        {
            Skip = "E2E: chybí E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD (admin se zakládá příkazem create-admin).";
        }
    }
}
