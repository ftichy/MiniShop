using Microsoft.AspNetCore.Hosting;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Škálování krok 2 a 3C: databáze pro jeden test – čerstvá PostgreSQL databáze (kopie šablony s migracemi všech modulů),
/// moduly v ní oddělují schémata stejně jako v produkci. Server spustí <see cref="PostgresTestServer"/> (Testcontainers)
/// jednou za běh testů – lokálně je potřeba Docker. V Dispose se databáze smaže, testy se tak navzájem neovlivní.
/// </summary>
public sealed class TestDatabases : IDisposable
{
    private readonly string _name;

    public TestDatabases()
    {
        (_name, var connectionString) = PostgresTestServer.CreateDatabase();
        Database = new ModuleDatabase(connectionString);
    }

    /// <summary>Databáze sdílená všemi moduly (jako ConnectionStrings:minishop v produkci).</summary>
    public ModuleDatabase Database { get; }

    // Pojmenované vlastnosti – testy čtou, kterou databázi modul dostává (všechny jsou stejné, oddělují je schémata).
    public ModuleDatabase Ordering => Database;
    public ModuleDatabase Inventory => Database;
    public ModuleDatabase Journeys => Database;
    public ModuleDatabase Accounts => Database;
    public ModuleDatabase Notifications => Database;
    public ModuleDatabase Payments => Database;
    public ModuleDatabase Reporting => Database;
    public ModuleDatabase Reviews => Database;

    /// <summary>Konfigurace služby v paměti (WebApplicationFactory) – stejný klíč jako v produkci: <c>ConnectionStrings:minishop</c>.</summary>
    public void Configure(IWebHostBuilder builder) =>
        builder.UseSetting($"ConnectionStrings:{ModuleDatabase.ConnectionName}", Database.ConnectionString);

    public void Dispose() => PostgresTestServer.DropDatabase(_name, Database.ConnectionString);
}
