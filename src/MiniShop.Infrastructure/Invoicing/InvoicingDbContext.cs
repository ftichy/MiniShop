using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Invoicing;
using MiniShop.Domain.Common;
using MiniShop.Domain.Invoicing;
using MiniShop.Infrastructure.Invoicing.Configurations;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Invoicing;

/// <summary>
/// E-shop krok 15: databáze modulu Invoicing – schéma invoicing: kopie objednávek, faktury, číselné řady, inbox a outbox
/// (<c>InvoiceIssuedV1</c>). Faktury se nedají přestavět z událostí (čísla!) – musí do zálohy.
/// </summary>
public sealed class InvoicingDbContext(DbContextOptions<InvoicingDbContext> options) : ModuleDbContext(options), IInvoicingUnitOfWork
{
    /// <summary>Schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "invoicing";

    public DbSet<BillableOrder> BillableOrders => Set<BillableOrder>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceNumberSeries> InvoiceNumberSeries => Set<InvoiceNumberSeries>();

    // Invoicing nemá doménové události – zprávu posílá handler rovnou přes publisher.
    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        throw new InvalidOperationException($"Modul Invoicing nemá doménové události ({domainEvent.GetType().Name}).");

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InvoicingDbContext).Assembly,
            type => type.Namespace == typeof(InvoiceConfiguration).Namespace);
}
