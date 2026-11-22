using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reporting;
using MiniShop.Domain.Common;
using MiniShop.Domain.Reporting;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting.Configurations;

namespace MiniShop.Infrastructure.Reporting;

/// <summary>
/// E-shop krok 11: databáze modulu Reporting – reporting.db: čtecí model přehledu tržeb (<see cref="SalesOrder"/>),
/// inbox (každá událost nejvýš jednou) a outbox (jen příkaz RebuildSalesReportV1).
/// </summary>
public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : ModuleDbContext(options), IReportingUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "reporting";

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    /// <summary>Proud B2: časy objednávek pro přehled Rychlost vyřízení (plní jen přehrávání proudu).</summary>
    public DbSet<OrderLeadTime> OrderLeadTimes => Set<OrderLeadTime>();

    /// <summary>Proud B2: záložky čtenářů proudu – ukládají se ve stejné transakci jako data projekce.</summary>
    public DbSet<StreamCheckpoint> StreamCheckpoints => Set<StreamCheckpoint>();

    /// <summary>E-shop krok 27A: naposledy odeslaná doporučení „Zákazníci také koupili“.</summary>
    public DbSet<ProductRecommendation> ProductRecommendations => Set<ProductRecommendation>();

    // Reporting nemá doménové události – kdyby nějaká vznikla, chyba se ukáže hned.
    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        throw new InvalidOperationException($"Modul Reporting nemá doménové události ({domainEvent.GetType().Name}).");

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly,
            type => type.Namespace == typeof(SalesOrderConfiguration).Namespace);
}
