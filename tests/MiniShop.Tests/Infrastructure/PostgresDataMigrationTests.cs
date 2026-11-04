using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Inventory;
using MiniShop.Domain.Journeys;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Payments;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Infrastructure.Reviews;
using MiniShop.Tests.Application;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Škálování krok 3: převod dat ze SQLite do PostgreSQL (<c>migrate-to-postgres</c>). Data vzniknou skutečnými scénáři
/// z <see cref="OrderServiceTests"/> nad SQLite (objednávky přes celou ságu, platby, kupony, vratky, hodnocení, přehled…),
/// pak se převedou a porovná se <b>obsah každé tabulky</b> – ne jen počty. Potřebuje PostgreSQL (Docker).
/// </summary>
public sealed class PostgresDataMigrationTests
{
    private static readonly ModuleDataMigration[] Modules =
    [
        ModuleDataMigration.For<OrderingDbContext>("Ordering", OrderingDbContext.Schema),
        ModuleDataMigration.For<InventoryDbContext>("Inventory", InventoryDbContext.Schema),
        ModuleDataMigration.For<JourneysDbContext>("Journeys", JourneysDbContext.Schema),
        ModuleDataMigration.For<NotificationsDbContext>("Notifications", NotificationsDbContext.Schema),
        ModuleDataMigration.For<PaymentsDbContext>("Payments", PaymentsDbContext.Schema),
        ModuleDataMigration.For<ReportingDbContext>("Reporting", ReportingDbContext.Schema),
        ModuleDataMigration.For<ReviewsDbContext>("Reviews", ReviewsDbContext.Schema),
        ModuleDataMigration.For<AccountsDbContext>("Accounts", AccountsDbContext.Schema)
    ];

    public static TheoryData<string> Scenarios =>
    [
        nameof(OrderServiceTests.Pokladna_TriProdukty_JednaObjednavkaProjdeSagouAKosikJePrazdny),
        nameof(OrderServiceTests.Platba_Zaplaceno_ObjednavkaPotvrzena),
        nameof(OrderServiceTests.Kupon_VPokladne_SlevaVPolozkachDopravaZPoSleve_PouzitiZapocteno),
        nameof(OrderServiceTests.Expedice_BaleniOdeslaniDoruceni_ZboziZustaneProdaneARezervaceVydana),
        nameof(OrderServiceTests.Vratka_Kartou_ZboziZpetPenizeZpetPrehledOdecte),
        nameof(OrderServiceTests.Hodnoceni_UpravaASkryti_PrumerSePrepocita),
        nameof(OrderServiceTests.Prehled_ZUdalosti_TrzbaJenZPlatnychObjednavek),
        nameof(OrderServiceTests.Notifikace_CelaCesta_KazdyStavPraveJedenEmailSOdkazem),
        nameof(OrderServiceTests.OpustenyKosik_SeSouhlasem_JednaPripominkaSOdkazy)
    ];

    [PostgresTheory]
    [MemberData(nameof(Scenarios))]
    public async Task Prevod_DataScenare_StejnyObsahVsechTabulek(string scenario)
    {
        using var sqlite = new TestDatabases(DatabaseProvider.Sqlite);
        using var postgres = new TestDatabases(DatabaseProvider.Postgres);
        var scenarioTests = new OrderServiceTests(sqlite);
        try
        {
            await (Task)typeof(OrderServiceTests).GetMethod(scenario)!.Invoke(scenarioTests, null)!;

            await MigrateAsync(sqlite, postgres, expectSuccess: true);

            await AssertSameContentAsync(sqlite, postgres);
        }
        finally
        {
            scenarioTests.Dispose();   // smaže i SQLite databáze
        }
    }

    [PostgresFact]
    public async Task Prevod_UctyAPrubehFormulare_ZablokovanyUcetZustaneZablokovany()
    {
        using var sqlite = new TestDatabases(DatabaseProvider.Sqlite);
        using var postgres = new TestDatabases(DatabaseProvider.Postgres);

        await using (var services = AccountsServices(sqlite.Accounts))
        {
            await AccountsDatabaseInitializer.InitializeAsync(services, new AdminBootstrapSettings("admin@example.com", "admin-heslo-123"));
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "jan@example.com", Email = "jan@example.com", DisplayName = "Jan", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, "jan-heslo-123")).Succeeded);
            await users.AddToRoleAsync(user, AccountRoles.User);
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);   // zablokovaný adminem
        }

        await using (var journeys = CreateContext<JourneysDbContext>(sqlite.Journeys, JourneysDbContext.Schema))
        {
            await journeys.Database.MigrateAsync();
            journeys.JourneyEvents.Add(JourneyEvent.OrderSubmitted(Guid.NewGuid(), 1500, 42, DateTime.UtcNow));
            await journeys.SaveChangesAsync();
        }

        await MigrateAsync(sqlite, postgres, expectSuccess: true);
        await AssertSameContentAsync(sqlite, postgres);

        await using (var services = AccountsServices(postgres.Accounts))
        {
            await using var scope = services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var jan = await users.FindByEmailAsync("jan@example.com");
            Assert.NotNull(jan);
            Assert.True(await users.CheckPasswordAsync(jan, "jan-heslo-123"));                // hash hesla přežil
            Assert.True(await users.IsInRoleAsync(jan, AccountRoles.User));
            Assert.Equal(DateTimeOffset.MaxValue.Year, (await users.GetLockoutEndDateAsync(jan))!.Value.Year);   // blokace přežila
        }
    }

    [PostgresFact]
    public async Task Prevod_Podruhe_JenSReplace_ANikdyPresNovaData()
    {
        using var sqlite = new TestDatabases(DatabaseProvider.Sqlite);
        using var postgres = new TestDatabases(DatabaseProvider.Postgres);
        var scenarioTests = new OrderServiceTests(sqlite);
        try
        {
            await scenarioTests.Pokladna_TriProdukty_JednaObjednavkaProjdeSagouAKosikJePrazdny();

            await MigrateAsync(sqlite, postgres, expectSuccess: true);

            // Podruhé bez --replace ne: v PostgreSQL mohla aplikace mezitím data jen upravit (změna hesla) – značka převodu to pozná.
            Assert.Contains("Modul už byl převeden", await MigrateAsync(sqlite, postgres, expectSuccess: false));
            await MigrateAsync(sqlite, postgres, expectSuccess: true, replace: true);   // vědomě znovu (např. po návratu na SQLite)

            // Aplikace už běží nad PostgreSQL: nová kategorie musí dostat nové ID (čítač identity posunutý za převedená data).
            int maxId;
            await using (var inventory = CreateContext<InventoryDbContext>(postgres.Inventory, InventoryDbContext.Schema))
            {
                maxId = await inventory.Categories.MaxAsync(c => c.Id);
                inventory.Categories.Add(new Category("Nová po převodu"));
                await inventory.SaveChangesAsync();
                Assert.True(await inventory.Categories.MaxAsync(c => c.Id) > maxId);
            }

            // … a další převod by ji přepsal – odmítne se i s --replace a nic nezmění.
            var output = await MigrateAsync(sqlite, postgres, expectSuccess: false, replace: true);
            Assert.Contains("už obsahuje data, která v SQLite nejsou", output);
            await using (var inventory = CreateContext<InventoryDbContext>(postgres.Inventory, InventoryDbContext.Schema))
            {
                Assert.True(await inventory.Categories.AnyAsync(c => c.Name == "Nová po převodu"));
            }
        }
        finally
        {
            scenarioTests.Dispose();
        }
    }

    private static async Task<string> MigrateAsync(TestDatabases sqlite, TestDatabases postgres, bool expectSuccess, bool replace = false)
    {
        var settings = Modules.ToDictionary(m => $"ConnectionStrings:{m.Module}", m => (string?)sqlite[m.Module].ConnectionString);
        settings[$"ConnectionStrings:{ModuleDatabase.PostgresConnectionName}"] = postgres.Ordering.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        using var output = new StringWriter();
        var exitCode = await MigrateToPostgresCommand.RunAsync(configuration, output, Modules, replace);

        Assert.True(expectSuccess == (exitCode == 0), output.ToString());
        return output.ToString();
    }

    /// <summary>Každá tabulka každého modulu: stejné řádky (po převodu typů) v SQLite i PostgreSQL.</summary>
    private static async Task AssertSameContentAsync(TestDatabases sqlite, TestDatabases postgres)
    {
        foreach (var module in Modules)
        {
            await using var source = module.CreateContext(sqlite[module.Module]);
            await using var target = module.CreateContext(postgres[module.Module]);
            var sourceTables = source.Model.GetRelationalModel().Tables.ToDictionary(t => t.Name);

            foreach (var table in target.Model.GetRelationalModel().Tables)
            {
                var columns = table.Columns.ToList();
                var expected = await ReadRowsAsync(source, sourceTables[table.Name], columns, convert: true);
                var actual = await ReadRowsAsync(target, table, columns, convert: false);
                Assert.True(expected.SequenceEqual(actual),
                    $"{module.Module}.{table.Name}: obsah se liší\nSQLite:\n{string.Join("\n", expected)}\nPostgreSQL:\n{string.Join("\n", actual)}");
            }
        }
    }

    private static async Task<List<string>> ReadRowsAsync(DbContext db, ITable table, List<IColumn> columns, bool convert)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            var name = table.Schema is null ? $"\"{table.Name}\"" : $"\"{table.Schema}\".\"{table.Name}\"";
            command.CommandText = $"SELECT {string.Join(", ", columns.Select(c => $"\"{c.Name}\""))} FROM {name}";
            var rows = new List<string>();
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(string.Join(" | ", columns.Select((column, i) =>
                {
                    var value = reader.GetValue(i);
                    return Normalize(convert ? SqliteToPostgresCopier.Convert(value, column) : value);
                })));
            }

            rows.Sort(StringComparer.Ordinal);   // pořadí řádků se v databázích liší (GUID jako text × uuid)
            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Společný tvar hodnoty. PostgreSQL ukládá čas na mikrosekundy (.NET na 100 ns) – porovnává se s touto přesností.</summary>
    private static string Normalize(object value) => value switch
    {
        DBNull => "NULL",
        // Bez Kind = UTC (všechny časy v MiniShopu; Npgsql tak vrací i „nekonečno“ = DateTime.MaxValue u zablokovaného účtu).
        DateTime time => "t" + ((time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time).Ticks / 10).ToString(CultureInfo.InvariantCulture),
        DateTimeOffset time => "t" + (time.UtcTicks / 10).ToString(CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.############################", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        byte[] bytes => Convert.ToBase64String(bytes),
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static TContext CreateContext<TContext>(ModuleDatabase database, string schema) where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseModuleDatabase(database, schema).Options)!;

    private static ServiceProvider AccountsServices(ModuleDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsInfrastructure(database);
        return services.BuildServiceProvider();
    }
}
