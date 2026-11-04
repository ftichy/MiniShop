using Microsoft.AspNetCore.Hosting;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Škálování krok 2: databáze modulů pro jeden test – podle proměnné prostředí <c>MINISHOP_TEST_DATABASE</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>Sqlite</c> (výchozí): každý modul vlastní SQLite databázi v paměti – rychlé, bez Dockeru.</item>
/// <item><c>Postgres</c>: všechny moduly jedna čerstvá PostgreSQL databáze (kopie šablony s migracemi), každý ve svém
/// schématu – stejně jako v produkci. Server spustí <see cref="PostgresTestServer"/> (Testcontainers) jednou za běh.</item>
/// </list>
/// Testy se navzájem neovlivní: každá instance má vlastní databáze a v Dispose je smaže.
/// </remarks>
public sealed class TestDatabases : IDisposable
{
    public const string EnvironmentVariable = "MINISHOP_TEST_DATABASE";

    /// <summary>Provider pro celý běh testů.</summary>
    public static DatabaseProvider Provider { get; } =
        Enum.TryParse<DatabaseProvider>(Environment.GetEnvironmentVariable(EnvironmentVariable), ignoreCase: true, out var provider)
            ? provider
            : DatabaseProvider.Sqlite;

    public static bool IsPostgres => Provider == DatabaseProvider.Postgres;

    private readonly Dictionary<string, InMemoryDatabase> _sqlite = new();
    private readonly string? _postgresDatabase;
    private readonly string? _postgresConnectionString;

    public TestDatabases()
    {
        if (IsPostgres)
        {
            (_postgresDatabase, _postgresConnectionString) = PostgresTestServer.CreateDatabase();
        }
    }

    public ModuleDatabase Ordering => this["Ordering"];
    public ModuleDatabase Inventory => this["Inventory"];
    public ModuleDatabase Journeys => this["Journeys"];
    public ModuleDatabase Accounts => this["Accounts"];
    public ModuleDatabase Notifications => this["Notifications"];
    public ModuleDatabase Payments => this["Payments"];
    public ModuleDatabase Reporting => this["Reporting"];
    public ModuleDatabase Reviews => this["Reviews"];

    /// <summary>Databáze modulu (jméno = connection string SQLite, např. "Ordering").</summary>
    public ModuleDatabase this[string module]
    {
        get
        {
            if (_postgresConnectionString is not null)
            {
                return ModuleDatabase.Postgres(_postgresConnectionString);
            }

            lock (_sqlite)
            {
                if (!_sqlite.TryGetValue(module, out var database))
                {
                    _sqlite[module] = database = new InMemoryDatabase();
                }

                return ModuleDatabase.Sqlite(database.ConnectionString);
            }
        }
    }

    /// <summary>
    /// Konfigurace služby v paměti (WebApplicationFactory) – stejné klíče jako v produkci:
    /// SQLite <c>ConnectionStrings:&lt;Modul&gt;</c>, PostgreSQL <c>Database:Provider</c> + <c>ConnectionStrings:minishop</c>.
    /// </summary>
    public void Configure(IWebHostBuilder builder, params string[] modules)
    {
        if (_postgresConnectionString is not null)
        {
            builder.UseSetting("Database:Provider", nameof(DatabaseProvider.Postgres));
            builder.UseSetting($"ConnectionStrings:{ModuleDatabase.PostgresConnectionName}", _postgresConnectionString);
            return;
        }

        foreach (var module in modules)
        {
            builder.UseSetting($"ConnectionStrings:{module}", this[module].ConnectionString);
        }
    }

    public void Dispose()
    {
        lock (_sqlite)
        {
            foreach (var database in _sqlite.Values)
            {
                database.Dispose();
            }

            _sqlite.Clear();
        }

        if (_postgresDatabase is not null)
        {
            PostgresTestServer.DropDatabase(_postgresDatabase, _postgresConnectionString!);
        }
    }
}
