using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Journeys;
using MiniShop.Domain.Journeys;
using MiniShop.Infrastructure.Journeys.Configurations;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Journeys;

/// <summary>
/// Databáze modulu Journeys – journeys.db (krok 12): jen proud událostí uživatele (tabulka JourneyEvents).
/// </summary>
/// <remarks>
/// Na rozdíl od Orderingu a Inventory nedědí z ModuleDbContext: modul neposílá ani nepřijímá zprávy,
/// takže nepotřebuje outbox ani inbox. Idempotenci zápisu hlídá unikátní EventId přímo v tabulce událostí.
/// </remarks>
public sealed class JourneysDbContext(DbContextOptions<JourneysDbContext> options) : DbContext(options), IJourneysUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "journeys";

    public DbSet<JourneyEvent> JourneyEvents => Set<JourneyEvent>();

    // Škálování krok 1: v PostgreSQL časy jako UTC a tabulky ve schématu journeys.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseModuleSchema(this);
        modelBuilder.ApplyConfiguration(new JourneyEventConfiguration());
    }
}
