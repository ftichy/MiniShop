using Microsoft.EntityFrameworkCore;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Payments;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Infrastructure.Reviews;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Škálování krok 2: jeden PostgreSQL v Dockeru (Testcontainers) pro celý běh testů. Spustí se při první potřebě,
/// po skončení procesu ho uklidí Testcontainers (kontejner „Ryuk“).
/// </summary>
/// <remarks>
/// Migrace všech modulů proběhnou jen jednou – do šablony <c>minishop_template</c>. Každý test dostane kopii
/// (<c>CREATE DATABASE … TEMPLATE</c>, desítky milisekund) a v Dispose ji smaže. Přirovnání: formulář se nevyplňuje
/// pro každého znovu, okopíruje se předvyplněný vzor.
/// </remarks>
internal static class PostgresTestServer
{
    /// <summary>Stejná verze jako v Compose (deploy/compose.yaml).</summary>
    public const string Image = "postgres:18-alpine";

    private const string TemplateDatabase = "minishop_template";

    private static readonly Lazy<string> AdminConnectionString = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    // PostgreSQL nedovolí kopírovat šablonu, když se z ní zrovna kopíruje jinde – kopie se dělají postupně.
    private static readonly object CreateLock = new();

    /// <summary>Nová databáze se všemi schématy a migracemi; vrací jméno a connection string.</summary>
    public static (string Name, string ConnectionString) CreateDatabase()
    {
        var admin = AdminConnectionString.Value;
        var name = $"test_{Guid.NewGuid():N}";
        lock (CreateLock)
        {
            Execute(admin, $"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"");
        }

        return (name, WithDatabase(admin, name));
    }

    public static void DropDatabase(string name, string connectionString)
    {
        using (var connection = new NpgsqlConnection(connectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        Execute(AdminConnectionString.Value, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static string Start()
    {
        var container = new PostgreSqlBuilder(Image)
            // Jen pro testy: rychlost před odolností při pádu (data po testu nikdo nepotřebuje), víc spojení pro paralelní testy.
            .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off", "-c", "max_connections=500")
            .Build();
        container.StartAsync().GetAwaiter().GetResult();

        var admin = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = "postgres",
            IncludeErrorDetail = true
        }.ConnectionString;

        Execute(admin, $"CREATE DATABASE \"{TemplateDatabase}\"");
        MigrateTemplate(new ModuleDatabase(WithDatabase(admin, TemplateDatabase)));
        NpgsqlConnection.ClearAllPools();   // šablona se nesmí kopírovat, dokud je k ní někdo připojený

        return admin;
    }

    private static void MigrateTemplate(ModuleDatabase database)
    {
        Migrate<OrderingDbContext>(database, OrderingDbContext.Schema);
        Migrate<InventoryDbContext>(database, InventoryDbContext.Schema);
        Migrate<JourneysDbContext>(database, JourneysDbContext.Schema);
        Migrate<NotificationsDbContext>(database, NotificationsDbContext.Schema);
        Migrate<PaymentsDbContext>(database, PaymentsDbContext.Schema);
        Migrate<ReportingDbContext>(database, ReportingDbContext.Schema);
        Migrate<ReviewsDbContext>(database, ReviewsDbContext.Schema);
        Migrate<AccountsDbContext>(database, AccountsDbContext.Schema);
    }

    private static void Migrate<TContext>(ModuleDatabase database, string schema) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseModuleDatabase(database, schema).Options;
        using var db = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        db.Database.Migrate();
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
