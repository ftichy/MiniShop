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
/// E-shop krok 1 a škálování krok 3C: databáze modulů se zakládají a aktualizují přes EF migrace (PostgreSQL, schéma na modul).
/// Hlídá, že nikdo nezapomněl na migraci, že každý modul má tabulky jen ve svém schématu a že produkční historie migrací
/// (jména migrací ze škálování kroku 1–3) zůstává platná i po přesunu migrací do MiniShop.Infrastructure (krok 3C).
/// </summary>
public sealed class DatabaseMigrationTests
{
    public static TheoryData<string> Modules =>
        ["Ordering", "Inventory", "Journeys", "Notifications", "Payments", "Reporting", "Reviews", "Accounts"];

    /// <summary>Migrace, které má produkční databáze zapsané v __EFMigrationsHistory (převod 4. 11. 2026).</summary>
    private static readonly Dictionary<string, string[]> ProductionHistory = new()
    {
        ["Ordering"] = ["20261104174524_InitialPostgres"],
        ["Inventory"] = ["20261104174529_InitialPostgres", "20261104184754_UncategorizedCategory"],
        ["Journeys"] = ["20261104174534_InitialPostgres"],
        ["Notifications"] = ["20261104174539_InitialPostgres"],
        ["Payments"] = ["20261104174544_InitialPostgres"],
        ["Reporting"] = ["20261104174549_InitialPostgres"],
        ["Reviews"] = ["20261104174553_InitialPostgres"],
        ["Accounts"] = ["20261104174558_InitialPostgres"]
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Model_NemaZmenyBezMigrace(string module)
    {
        // Selže, když se změní entita nebo konfigurace a chybí migrace (dotnet ef migrations add … --project src/MiniShop.Infrastructure).
        // Server k tomu není potřeba – model se jen porovná se snapshotem.
        using var db = CreateDbContext(module, new ModuleDatabase("Host=localhost;Database=minishop"));

        Assert.False(db.Database.HasPendingModelChanges(), $"Model modulu {module} se změnil – chybí migrace.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Migrace_ProdukcniHistorieZustavaPlatna(string module)
    {
        // Produkce má tyto migrace zapsané jako provedené. Kdyby se některá přejmenovala nebo zmizela, EF by ji
        // chtěl spustit znovu (nebo by nesouhlasila historie) – proto se jména migrací nikdy nemění.
        using var db = CreateDbContext(module, new ModuleDatabase("Host=localhost;Database=minishop"));

        var migrations = db.Database.GetMigrations().ToList();
        Assert.Equal(ProductionHistory[module], migrations.Take(ProductionHistory[module].Length));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void TabulkyModuluJsouVeVlastnimSchematu(string module)
    {
        using var db = CreateDbContext(module, new ModuleDatabase("Host=localhost;Database=minishop"));

        var schemas = db.Model.GetEntityTypes().Select(e => e.GetSchema()).Distinct().ToList();

        Assert.Equal([module.ToLowerInvariant()], schemas);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task Migrace_OpakovaneSpusteni_NicNezmeni(string module)
    {
        // Start aplikace volá migraci pokaždé – nad databází se všemi migracemi už nemá co dělat.
        using var databases = new TestDatabases();
        await using var db = CreateDbContext(module, databases.Database);

        await DatabaseMigrator.MigrateAsync(db);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public void Konfigurace_ConnectionStringMinishop()
    {
        var database = ModuleDatabase.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:minishop"] = "Host=postgres;Database=minishop" })
            .Build());
        Assert.Equal("Host=postgres;Database=minishop", database.ConnectionString);
        Assert.Equal("PostgreSQL", database.ToString());   // connection string (heslo) se nevypíše

        var missing = Assert.Throws<InvalidOperationException>(() =>
            ModuleDatabase.FromConfiguration(new ConfigurationBuilder().Build()));
        Assert.Contains("minishop", missing.Message);
    }

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
