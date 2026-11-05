using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// Databáze modulů (škálování krok 1, od kroku 3C jen PostgreSQL): všechny moduly sdílí jednu databázi
/// (<c>ConnectionStrings:minishop</c>, dodá ji Aspire nebo Compose) a každý má vlastní schéma (ordering, inventory, …)
/// – „schéma = vlastní databáze modulu“. Moduly se registrují přes <see cref="ModuleDatabaseExtensions.UseModuleDatabase"/>.
/// </summary>
public sealed record ModuleDatabase(string ConnectionString)
{
    /// <summary>Jméno connection stringu (= jméno databáze v Aspire).</summary>
    public const string ConnectionName = "minishop";

    public static ModuleDatabase FromConfiguration(IConfiguration configuration) =>
        new(configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Chybí connection string '{ConnectionName}' (databáze PostgreSQL)."));

    /// <summary>
    /// Škálování krok 5: samostatné spojení mimo EF Core (zámek vedoucí instance drží vlastní spojení po celou dobu vedení).
    /// </summary>
    /// <remarks>
    /// <paramref name="pooling"/> = false: zavření spojení ho opravdu zavře (a server uvolní zámky relace). Ze sdíleného
    /// poolu by se spojení jen vrátilo – zámek by zůstal držený, dokud by ho Npgsql znovu nepoužil.
    /// </remarks>
    public NpgsqlConnection CreateConnection(bool pooling = true) =>
        new(new NpgsqlConnectionStringBuilder(ModuleDatabaseExtensions.ConnectionString(ConnectionString)) { Pooling = pooling }
            .ConnectionString);

    public override string ToString() => "PostgreSQL";   // connection string obsahuje heslo – do logu nepatří
}

/// <summary>Nastavení EF Core pro databázi modulu (registrace, design-time továrny, kontexty).</summary>
public static class ModuleDatabaseExtensions
{
    /// <summary>
    /// PostgreSQL se schématem modulu <paramref name="schema"/> a vlastní tabulkou historie migrací v něm –
    /// moduly se tak migrují nezávisle (Web a Inventory.Api zvlášť).
    /// </summary>
    public static DbContextOptionsBuilder UseModuleDatabase(
        this DbContextOptionsBuilder options, ModuleDatabase database, string schema) =>
        options.UseNpgsql(ConnectionString(database.ConnectionString),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema));

    /// <summary>
    /// Škálování krok 3: Npgsql by se jinak při každém připojení pokusil o šifrování přes Kerberos (GSSAPI) – v image
    /// služeb knihovna libgssapi_krb5 není a do konzole by psal chybu. MiniShop se přihlašuje heslem, Kerberos nepoužívá.
    /// </summary>
    internal static string ConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }.ConnectionString;

    /// <summary>
    /// Do <c>OnModelCreating</c> každého kontextu: tabulky modulu do jeho schématu (stejného jako historie migrací
    /// z <see cref="UseModuleDatabase"/> – schéma je tak zapsané jen na jednom místě).
    /// </summary>
    public static void UseModuleSchema(this ModelBuilder modelBuilder, DbContext context)
    {
        var schema = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).MigrationsHistoryTableSchema;
        if (schema is not null)
        {
            modelBuilder.HasDefaultSchema(schema);
        }
    }

    /// <summary>
    /// Do <c>ConfigureConventions</c> každého kontextu: PostgreSQL ukládá DateTime jako <c>timestamp with time zone</c>
    /// a odmítne čas bez DateTimeKind.Utc. Všechny časy v MiniShopu jsou UTC – při zápisu je označíme, při čtení také.
    /// </summary>
    public static void UseUtcDateTimes(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value
            : value.Kind == DateTimeKind.Local ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
