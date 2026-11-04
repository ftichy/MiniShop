using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>Škálování krok 1: na jaké databázi modul běží (konfigurace <c>Database:Provider</c>).</summary>
public enum DatabaseProvider
{
    /// <summary>Soubor na disku (ordering.db, …) – výchozí, dnes v produkci.</summary>
    Sqlite,

    /// <summary>Databázový server – jedna databáze, každý modul ve vlastním schématu.</summary>
    Postgres
}

/// <summary>
/// Škálování krok 1: databáze jednoho modulu – provider a connection string. Moduly se registrují stejně
/// (<see cref="ModuleDatabaseExtensions.UseModuleDatabase"/>), rozdíl mezi SQLite a PostgreSQL je jen tady.
/// </summary>
/// <remarks>
/// SQLite: každý modul má vlastní soubor (<c>ConnectionStrings:Ordering</c> …).
/// PostgreSQL: všechny moduly sdílí jednu databázi (<c>ConnectionStrings:minishop</c>, dodá ji Aspire nebo Compose)
/// a každý má vlastní schéma (ordering, inventory, …) – „schéma = vlastní databáze modulu“.
/// </remarks>
public sealed record ModuleDatabase(DatabaseProvider Provider, string ConnectionString)
{
    /// <summary>Jméno connection stringu sdílené PostgreSQL databáze (= jméno databáze v Aspire).</summary>
    public const string PostgresConnectionName = "minishop";

    public static ModuleDatabase Sqlite(string connectionString) => new(DatabaseProvider.Sqlite, connectionString);

    public static ModuleDatabase Postgres(string connectionString) => new(DatabaseProvider.Postgres, connectionString);

    /// <summary>Starší volání a testy předávají jen connection string SQLite.</summary>
    public static implicit operator ModuleDatabase(string sqliteConnectionString) => Sqlite(sqliteConnectionString);

    /// <summary>Databáze modulu podle konfigurace; <paramref name="module"/> = jméno connection stringu SQLite ("Ordering").</summary>
    public static ModuleDatabase FromConfiguration(IConfiguration configuration, string module)
    {
        var provider = configuration.GetValue("Database:Provider", DatabaseProvider.Sqlite);
        var name = provider == DatabaseProvider.Postgres ? PostgresConnectionName : module;
        var connectionString = configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException(
                $"Chybí connection string '{name}' (Database:Provider = {provider}, modul {module}).");

        return new ModuleDatabase(provider, connectionString);
    }

    public override string ToString() => Provider.ToString();   // connection string obsahuje heslo – do logu nepatří
}

/// <summary>Škálování krok 1: nastavení EF Core pro databázi modulu (registrace, design-time továrny, kontexty).</summary>
public static class ModuleDatabaseExtensions
{
    /// <summary>Projekt s migracemi pro PostgreSQL – migrace SQLite zůstávají v MiniShop.Infrastructure beze změny.</summary>
    public const string PostgresMigrationsAssembly = "MiniShop.Infrastructure.PostgreSql";

    /// <summary>
    /// Provider podle <paramref name="database"/>. U PostgreSQL má modul vlastní schéma <paramref name="schema"/>
    /// i vlastní tabulku historie migrací v něm – moduly se tak migrují nezávisle (Web a Inventory.Api zvlášť).
    /// </summary>
    public static DbContextOptionsBuilder UseModuleDatabase(
        this DbContextOptionsBuilder options, ModuleDatabase database, string schema) =>
        database.Provider switch
        {
            DatabaseProvider.Sqlite => options.UseSqlite(database.ConnectionString),
            DatabaseProvider.Postgres => options.UseNpgsql(PostgresConnectionString(database.ConnectionString), npgsql => npgsql
                .MigrationsAssembly(PostgresMigrationsAssembly)
                .MigrationsHistoryTable("__EFMigrationsHistory", schema)),
            _ => throw new ArgumentOutOfRangeException(nameof(database), database.Provider, "Neznámý provider databáze.")
        };

    /// <summary>
    /// Škálování krok 3: Npgsql by se jinak při každém připojení pokusil o šifrování přes Kerberos (GSSAPI) – v image
    /// služeb knihovna libgssapi_krb5 není a do konzole by psal chybu. MiniShop se přihlašuje heslem, Kerberos nepoužívá.
    /// </summary>
    private static string PostgresConnectionString(string connectionString) =>
        new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = Npgsql.GssEncryptionMode.Disable }.ConnectionString;

    /// <summary>
    /// Do <c>OnModelCreating</c> každého kontextu: u PostgreSQL dá tabulky modulu do jeho schématu (stejného jako historie
    /// migrací z <see cref="UseModuleDatabase"/>). SQLite schémata nezná – model i migrace SQLite zůstávají beze změny.
    /// </summary>
    public static void UseModuleSchema(this ModelBuilder modelBuilder, DbContext context)
    {
        if (!context.Database.IsNpgsql())
        {
            return;
        }

        var schema = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).MigrationsHistoryTableSchema;
        if (schema is not null)
        {
            modelBuilder.HasDefaultSchema(schema);
        }
    }

    /// <summary>
    /// Do <c>ConfigureConventions</c> každého kontextu: PostgreSQL ukládá DateTime jako <c>timestamp with time zone</c>
    /// a odmítne čas bez DateTimeKind.Utc. Všechny časy v MiniShopu jsou UTC (i když je SQLite načte bez Kind),
    /// proto je při zápisu označíme jako UTC – chování je stejné jako se SQLite.
    /// </summary>
    public static void UseUtcDateTimes(this ModelConfigurationBuilder configurationBuilder, DbContext context)
    {
        if (context.Database.IsNpgsql())
        {
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value
            : value.Kind == DateTimeKind.Local ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
