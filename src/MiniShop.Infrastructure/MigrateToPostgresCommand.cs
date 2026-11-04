using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure;

/// <summary>Škálování krok 3: jeden modul pro převod – jméno connection stringu SQLite a jak vytvořit jeho DbContext.</summary>
public sealed record ModuleDataMigration(string Module, string Schema, Func<ModuleDatabase, DbContext> CreateContext)
{
    public static ModuleDataMigration For<TContext>(string module, string schema) where TContext : DbContext =>
        new(module, schema, database => (TContext)Activator.CreateInstance(typeof(TContext),
            new DbContextOptionsBuilder<TContext>().UseModuleDatabase(database, schema).Options)!);
}

/// <summary>
/// Škálování krok 3: <c>dotnet MiniShop.Web.dll migrate-to-postgres</c> (a totéž pro Inventory.Api) – převede data modulů služby
/// ze SQLite (<c>ConnectionStrings:&lt;Modul&gt;</c>) do PostgreSQL (<c>ConnectionStrings:minishop</c>), vypíše počty řádků a skončí.
/// Web se nespustí. Spouští se při zastavené službě (plánovaná odstávka) – nic jiného do databází nesmí zapisovat.
/// Modul už převedený se znovu převede jen s <c>--replace</c> (např. po návratu na SQLite) – viz <see cref="SqliteToPostgresCopier"/>.
/// </summary>
public static class MigrateToPostgresCommand
{
    public const string Name = "migrate-to-postgres";

    public const string ReplaceOption = "--replace";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    public static bool IsReplace(string[] args) => args.Contains(ReplaceOption);

    /// <returns>Návratový kód procesu: 0 = vše převedeno a počty sedí.</returns>
    public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output,
        IReadOnlyList<ModuleDataMigration> modules, bool replace = false, CancellationToken cancellationToken = default)
    {
        var postgres = configuration.GetConnectionString(ModuleDatabase.PostgresConnectionName);
        if (string.IsNullOrWhiteSpace(postgres))
        {
            await output.WriteLineAsync($"Chybí ConnectionStrings:{ModuleDatabase.PostgresConnectionName} (cílová PostgreSQL databáze).");
            return 2;
        }

        var failed = false;
        foreach (var module in modules)
        {
            var sqlite = configuration.GetConnectionString(module.Module);
            if (string.IsNullOrWhiteSpace(sqlite))
            {
                await output.WriteLineAsync($"Chybí ConnectionStrings:{module.Module} (zdrojová SQLite databáze).");
                return 2;
            }

            await output.WriteLineAsync($"== {module.Module} → schéma {module.Schema}");
            try
            {
                await using var source = module.CreateContext(ModuleDatabase.Sqlite(sqlite));
                await using var target = module.CreateContext(ModuleDatabase.Postgres(postgres));
                var results = await SqliteToPostgresCopier.CopyAsync(source, target, replace, cancellationToken);
                foreach (var result in results)
                {
                    await output.WriteLineAsync($"   {result.Table,-28} SQLite {result.SourceRows,7}   PostgreSQL {result.TargetRows,7}   OK");
                }

                await output.WriteLineAsync($"   celkem {results.Sum(r => r.SourceRows)} řádků v {results.Count} tabulkách");
            }
            catch (Exception ex)
            {
                // Modul se převádí v jedné transakci – při chybě v PostgreSQL nic nezůstane, SQLite se nemění.
                failed = true;
                await output.WriteLineAsync($"   CHYBA – modul nepřeveden: {ex.Message}");
            }
        }

        await output.WriteLineAsync(failed ? "Převod NEPROBĚHL celý – viz chyby výše." : "Převod dokončen, všechny počty souhlasí.");
        return failed ? 1 : 0;
    }
}
