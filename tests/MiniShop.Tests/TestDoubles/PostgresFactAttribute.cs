namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Škálování krok 3: test, který potřebuje skutečný PostgreSQL (Docker). Běží jen ve variantě
/// <c>MINISHOP_TEST_DATABASE=Postgres</c> (CI job „Testy PostgreSQL“), jinak se přeskočí – výchozí testy Docker nepotřebují.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!TestDatabases.IsPostgres)
        {
            Skip = $"Potřebuje PostgreSQL – spusťte s {TestDatabases.EnvironmentVariable}=Postgres.";
        }
    }
}
