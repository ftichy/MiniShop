using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Invoicing;
using MiniShop.Domain.Invoicing;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Infrastructure.Invoicing;

public sealed class BillableOrderRepository(InvoicingDbContext db) : IBillableOrderRepository
{
    public Task<BillableOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.BillableOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

    public async Task RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default) =>
        db.BillableOrders.RemoveRange(await db.BillableOrders.Where(o => o.CustomerId == customerId).ToListAsync(cancellationToken));

    public void Add(BillableOrder order) => db.BillableOrders.Add(order);
}

public sealed class InvoiceRepository(InvoicingDbContext db) : IInvoiceRepository
{
    public Task<Invoice?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.OrderId == orderId, cancellationToken);

    public Task<bool> ExistsForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.Invoices.AnyAsync(i => i.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        await db.Invoices.AsNoTracking().OrderByDescending(i => i.Id).Take(count).ToListAsync(cancellationToken);

    public void Add(Invoice invoice) => db.Invoices.Add(invoice);
}

public sealed class InvoiceNumberSeriesRepository(InvoicingDbContext db) : IInvoiceNumberSeriesRepository
{
    public Task<InvoiceNumberSeries?> GetAsync(int year, CancellationToken cancellationToken = default) =>
        db.InvoiceNumberSeries.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);

    public void Add(InvoiceNumberSeries series) => db.InvoiceNumberSeries.Add(series);
}

public sealed class CreditNoteRepository(InvoicingDbContext db) : ICreditNoteRepository
{
    public Task<CreditNote?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.CreditNotes.AsNoTracking().FirstOrDefaultAsync(c => c.OrderId == orderId, cancellationToken);

    public Task<bool> ExistsForOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.CreditNotes.AnyAsync(c => c.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyDictionary<int, CreditNote>> GetByOrdersAsync(
        IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default) =>
        await db.CreditNotes.AsNoTracking().Where(c => orderIds.Contains(c.OrderId)).ToDictionaryAsync(c => c.OrderId, cancellationToken);

    public void Add(CreditNote creditNote) => db.CreditNotes.Add(creditNote);
}

public sealed class CreditNoteNumberSeriesRepository(InvoicingDbContext db) : ICreditNoteNumberSeriesRepository
{
    public Task<CreditNoteNumberSeries?> GetAsync(int year, CancellationToken cancellationToken = default) =>
        db.CreditNoteNumberSeries.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);

    public void Add(CreditNoteNumberSeries series) => db.CreditNoteNumberSeries.Add(series);
}

/// <summary>E-shop krok 15: zprávy z modulu Invoicing jdou do outboxu ve schématu invoicing.</summary>
public sealed class InvoicingMessagePublisher(InvoicingDbContext db)
    : OutboxIntegrationMessagePublisher(db), IInvoicingMessagePublisher;
