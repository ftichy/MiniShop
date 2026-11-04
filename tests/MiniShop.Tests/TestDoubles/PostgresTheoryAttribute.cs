namespace MiniShop.Tests.TestDoubles;

/// <summary>Škálování krok 3: jako <see cref="PostgresFactAttribute"/>, jen pro testy s parametry.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (!TestDatabases.IsPostgres)
        {
            Skip = $"Potřebuje PostgreSQL – spusťte s {TestDatabases.EnvironmentVariable}=Postgres.";
        }
    }
}
