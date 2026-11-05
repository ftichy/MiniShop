using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Payments;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Common;
using MiniShop.Domain.Payments;
using MiniShop.Domain.Payments.Events;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Payments.Configurations;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Payments;

/// <summary>
/// E-shop krok 8: databáze modulu Payments – payments.db: platby + vlastní outbox a inbox (účastník ságy).
/// </summary>
/// <remarks>
/// Doménové události platby se při uložení rovnou převedou na integrační zprávy (jako v Inventory) – Payments nemá
/// vlastní doménové handlery. EventId integrační zprávy = EventId doménové události.
/// </remarks>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : ModuleDbContext(options), IPaymentsUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "payments";

    public DbSet<Payment> Payments => Set<Payment>();

    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) => domainEvent switch
    {
        PaymentSucceeded e =>
            [OutboxMessageFactory.Create(new PaymentSucceededV1(e.EventId, e.OccurredAtUtc, e.Payment.OrderId, e.Payment.Amount))],
        PaymentFailed e =>
            [OutboxMessageFactory.Create(new PaymentFailedV1(e.EventId, e.OccurredAtUtc, e.Payment.OrderId, e.Payment.FailureReason!))],
        PaymentRefunded e =>
            [OutboxMessageFactory.Create(new PaymentRefundedV1(e.EventId, e.OccurredAtUtc, e.Payment.OrderId, e.Payment.Amount))],
        _ => throw new InvalidOperationException(
            $"Událost {domainEvent.GetType().Name} nemá převod do outboxu. Doplňte ji do {nameof(PaymentsDbContext)}.")
    };

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly,
            type => type.Namespace == typeof(PaymentConfiguration).Namespace);
}
