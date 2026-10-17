using Microsoft.Data.Sqlite;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Pojmenovaná SQLite databáze v paměti, sdílená mezi více připojeními.
/// Každý scope (= nový DbContext) se k ní připojí zvlášť, jako v aplikaci k souboru minishop.db.
/// Databáze žije, dokud je otevřené "udržovací" připojení – tedy do Dispose.
/// </summary>
public sealed class InMemoryDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public InMemoryDatabase()
    {
        ConnectionString = $"Data Source=minishop-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
    }

    public string ConnectionString { get; }

    public void Dispose() => _keepAlive.Dispose();
}
