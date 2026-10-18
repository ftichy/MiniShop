using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Journeys;
using MiniShop.Domain.Journeys;
using MiniShop.Infrastructure.Journeys.Configurations;

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
    public DbSet<JourneyEvent> JourneyEvents => Set<JourneyEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new JourneyEventConfiguration());
}
