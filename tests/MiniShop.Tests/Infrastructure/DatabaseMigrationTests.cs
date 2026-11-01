using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MiniShop.Domain.Journeys;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// E-shop krok 1: databáze modulů se zakládají a aktualizují přes EF migrace (dřív EnsureCreated).
/// Hlídá tři věci: že nikdo nezapomněl na migraci, že první migrace odpovídá schématu z EnsureCreated
/// (jen tak je baseline produkčních databází správná) a že stará databáze migrací projde bez ztráty dat.
/// </summary>
public sealed class DatabaseMigrationTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> Modules => ["Ordering", "Inventory", "Journeys", "Accounts"];

    [Theory]
    [MemberData(nameof(Modules))]
    public void Model_NemaZmenyBezMigrace(string module)
    {
        // Selže, když se změní entita nebo konfigurace a chybí nová migrace (dotnet ef migrations add …).
        using var database = new InMemoryDatabase();
        using var db = CreateDbContext(module, database);

        Assert.False(db.Database.HasPendingModelChanges(), $"Model modulu {module} se změnil – chybí migrace.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public async Task Migrace_VytvoriStejneSchemaJakoEnsureCreated(string module)
    {
        // Produkční databáze vznikly přes EnsureCreated a baseline jim zapíše první migraci jako provedenou.
        // To je správně jen tehdy, když migrace vytvoří přesně stejné tabulky, sloupce a indexy.
        using var created = new InMemoryDatabase();
        using var migrated = new InMemoryDatabase();
        await using (var db = CreateDbContext(module, created))
        {
            await db.Database.EnsureCreatedAsync();
        }

        await using (var db = CreateDbContext(module, migrated))
        {
            await MigrateAsync(module, db);
        }

        Assert.NotEmpty(Schema(created));
        Assert.Equal(Schema(created), Schema(migrated));
    }

    // Stará databáze = schéma PRVNÍ migrace bez historie migrací – tak vypadala produkce před e-shop krokem 1.
    // Baseline zapíše první migraci jako provedenou a novější (e-shop krok 2) se pak opravdu spustí – i s ručními úpravami.

    [Fact]
    public async Task Ordering_StaraDatabaze_ProjdeMigraci_DataZustanou()
    {
        using var database = new InMemoryDatabase();
        await using (var db = CreateDbContext<OrderingDbContext>(database))
        {
            await CreateOldDatabaseAsync(db);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO CatalogProducts (Id, Name, Price, IsRemoved, ChangedAtUtc) " +
                "VALUES (7, 'Klávesnice', '2490.0', 0, '2026-10-30 12:00:00')");
        }

        await using (var db = CreateDbContext<OrderingDbContext>(database))
        {
            await OrderingDatabaseInitializer.MigrateAsync(db);

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var product = await db.CatalogProducts.SingleAsync();
            Assert.Equal("Klávesnice", product.Name);
            // E-shop krok 2: dostupnost neznámá (doplní ji obnova katalogu), hledat jde aspoň podle názvu.
            Assert.Equal(CatalogAvailability.Unknown, product.Availability);
            Assert.Equal("klávesnice", product.SearchText);
        }
    }

    [Fact]
    public async Task Inventory_StaraDatabaze_ProjdeMigraci_DataZustanou()
    {
        using var database = new InMemoryDatabase();
        await using (var db = CreateDbContext<InventoryDbContext>(database))
        {
            await CreateOldDatabaseAsync(db);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Products (Name, Description, Price, StockQuantity, ChangedAtUtc) " +
                "VALUES ('Myš', NULL, '890.0', 40, '2026-10-30 12:00:00')");
        }

        await using (var db = CreateDbContext<InventoryDbContext>(database))
        {
            await InventoryDatabaseInitializer.MigrateAsync(db);

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var product = await db.Products.Include(p => p.Category).SingleAsync();
            Assert.Equal(40, product.StockQuantity);
            // E-shop krok 2: dosavadní produkty spadnou do kategorie „Nezařazeno“, čas dostupnosti = čas poslední změny.
            Assert.Equal("Nezařazeno", product.Category.Name);
            Assert.Equal(product.ChangedAtUtc, product.AvailabilityChangedAtUtc);
        }
    }

    [Fact]
    public async Task Journeys_StaraDatabaze_ProjdeMigraci_DataZustanou()
    {
        using var database = new InMemoryDatabase();
        await using (var db = CreateDbContext<JourneysDbContext>(database))
        {
            await CreateOldDatabaseAsync(db);
            db.JourneyEvents.Add(JourneyEvent.OrderSubmitted(Guid.NewGuid(), 1500, 42, Now));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext<JourneysDbContext>(database))
        {
            await JourneysDatabaseInitializer.MigrateAsync(db);

            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(42, (await db.JourneyEvents.SingleAsync()).OrderId);
        }
    }
    [Theory]
    [MemberData(nameof(Modules))]
    public async Task Migrace_OpakovaneSpusteni_NicNezmeni(string module)
    {
        // Start aplikace volá migraci pokaždé – podruhé už nemá co dělat.
        using var database = new InMemoryDatabase();
        await using var db = CreateDbContext(module, database);

        await MigrateAsync(module, db);
        await MigrateAsync(module, db);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private static DbContext CreateDbContext(string module, InMemoryDatabase database) => module switch
    {
        "Ordering" => CreateDbContext<OrderingDbContext>(database),
        "Inventory" => CreateDbContext<InventoryDbContext>(database),
        "Journeys" => CreateDbContext<JourneysDbContext>(database),
        "Accounts" => CreateDbContext<AccountsDbContext>(database),
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    private static Task MigrateAsync(string module, DbContext db) => db switch
    {
        OrderingDbContext ordering => OrderingDatabaseInitializer.MigrateAsync(ordering),
        InventoryDbContext inventory => InventoryDatabaseInitializer.MigrateAsync(inventory),
        JourneysDbContext journeys => JourneysDatabaseInitializer.MigrateAsync(journeys),
        AccountsDbContext accounts => AccountsDatabaseInitializer.MigrateAsync(accounts),
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    /// <summary>Databáze ve tvaru první migrace a bez historie migrací (jako z EnsureCreated v době první migrace).</summary>
    private static async Task CreateOldDatabaseAsync(DbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().First());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory");
    }

    private static TContext CreateDbContext<TContext>(InMemoryDatabase database) where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseSqlite(new SqliteConnection(database.ConnectionString)).Options)!;

    /// <summary>Tabulky, sloupce a indexy databáze (bez historie migrací) – pro porovnání dvou schémat.</summary>
    private static List<string> Schema(InMemoryDatabase database)
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.type || ' ' || m.name || ': ' || COALESCE(
                -- sloupce podle abecedy: migrace přidává nové sloupce na konec tabulky, na pořadí nezáleží
                (SELECT group_concat(col, ', ') FROM (
                    SELECT c.name || ' ' || c.type || CASE c."notnull" WHEN 1 THEN ' NOT NULL' ELSE '' END
                           || CASE c.pk WHEN 0 THEN '' ELSE ' PK' END AS col
                    FROM pragma_table_info(m.name) c WHERE m.type = 'table' ORDER BY c.name)),
                m.sql,   -- index: CREATE [UNIQUE] INDEX … ON tabulka (sloupce)
                '')
            FROM sqlite_master m
            WHERE m.name NOT LIKE '__EFMigrations%' AND m.name NOT LIKE 'sqlite_%'
            ORDER BY m.type, m.name
            """;
        using var reader = command.ExecuteReader();
        var schema = new List<string>();
        while (reader.Read())
        {
            schema.Add(reader.GetString(0));
        }

        return schema;
    }
}
