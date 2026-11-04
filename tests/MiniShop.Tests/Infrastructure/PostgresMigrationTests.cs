using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Payments;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Infrastructure.Reviews;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Škálování krok 1: každý kontext má dvě sady migrací – SQLite (MiniShop.Infrastructure) a PostgreSQL
/// (MiniShop.Infrastructure.PostgreSql). Tady se hlídá ta druhá: že nikdo nezapomněl na migraci pro PostgreSQL
/// a že každý modul má tabulky jen ve svém schématu. Server PostgreSQL k tomu není potřeba (model se jen porovná se snapshotem).
/// </summary>
public sealed class PostgresMigrationTests
{
    public static TheoryData<string> Modules =>
        ["Ordering", "Inventory", "Journeys", "Notifications", "Payments", "Reporting", "Reviews", "Accounts"];

    [Theory]
    [MemberData(nameof(Modules))]
    public void Postgres_ModelNemaZmenyBezMigrace(string module)
    {
        // Selže, když se změní entita a chybí migrace pro PostgreSQL (dotnet ef migrations add … --project src/MiniShop.Infrastructure.PostgreSql).
        using var db = CreateDbContext(module, ModuleDatabase.Postgres("Host=localhost;Database=minishop"));

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges(), $"Model modulu {module} se změnil – chybí migrace pro PostgreSQL.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Postgres_TabulkyModuluJsouVeVlastnimSchematu(string module)
    {
        using var db = CreateDbContext(module, ModuleDatabase.Postgres("Host=localhost;Database=minishop"));

        var schemas = db.Model.GetEntityTypes().Select(e => e.GetSchema()).Distinct().ToList();

        Assert.Equal([module.ToLowerInvariant()], schemas);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Sqlite_BezSchematu_MigraceZustavajiPuvodni(string module)
    {
        // Produkce běží na SQLite – jeho model i migrace se kvůli PostgreSQL nesmí změnit.
        using var database = new InMemoryDatabase();
        using var db = CreateDbContext(module, ModuleDatabase.Sqlite(database.ConnectionString));

        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Null(entity.GetSchema()));
        Assert.DoesNotContain(db.Database.GetMigrations(), migration => migration.EndsWith("_InitialPostgres"));
    }

    [Fact]
    public void Konfigurace_VychoziSqlite_PostgresSdilenaDatabaze()
    {
        var sqlite = ModuleDatabase.FromConfiguration(Configuration(new()
        {
            ["ConnectionStrings:Ordering"] = "Data Source=ordering.db"
        }), "Ordering");
        Assert.Equal(ModuleDatabase.Sqlite("Data Source=ordering.db"), sqlite);

        var postgres = ModuleDatabase.FromConfiguration(Configuration(new()
        {
            ["Database:Provider"] = "Postgres",
            ["ConnectionStrings:Ordering"] = "Data Source=ordering.db",
            ["ConnectionStrings:minishop"] = "Host=postgres;Database=minishop"
        }), "Ordering");
        Assert.Equal(ModuleDatabase.Postgres("Host=postgres;Database=minishop"), postgres);
        Assert.Equal("Postgres", postgres.ToString());   // connection string (heslo) se nevypíše

        var missing = Assert.Throws<InvalidOperationException>(() => ModuleDatabase.FromConfiguration(
            Configuration(new() { ["Database:Provider"] = "Postgres" }), "Ordering"));
        Assert.Contains("minishop", missing.Message);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static DbContext CreateDbContext(string module, ModuleDatabase database) => module switch
    {
        "Ordering" => Create<OrderingDbContext>(database, OrderingDbContext.Schema),
        "Inventory" => Create<InventoryDbContext>(database, InventoryDbContext.Schema),
        "Journeys" => Create<JourneysDbContext>(database, JourneysDbContext.Schema),
        "Notifications" => Create<NotificationsDbContext>(database, NotificationsDbContext.Schema),
        "Payments" => Create<PaymentsDbContext>(database, PaymentsDbContext.Schema),
        "Reporting" => Create<ReportingDbContext>(database, ReportingDbContext.Schema),
        "Reviews" => Create<ReviewsDbContext>(database, ReviewsDbContext.Schema),
        "Accounts" => Create<AccountsDbContext>(database, AccountsDbContext.Schema),
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    private static TContext Create<TContext>(ModuleDatabase database, string schema) where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseModuleDatabase(database, schema).Options)!;
}
