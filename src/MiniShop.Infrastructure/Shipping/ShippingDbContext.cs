using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Shipping;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Common;
using MiniShop.Domain.Shipping;
using MiniShop.Domain.Shipping.Events;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Shipping.Configurations;

namespace MiniShop.Infrastructure.Shipping;

/// <summary>
/// E-shop krok 26B: databáze modulu Shipping – schéma shipping: zásilky + vlastní outbox a inbox.
/// Doménové události se při uložení rovnou převedou na integrační zprávy (jako Payments).
/// </summary>
public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : ModuleDbContext(options), IShippingUnitOfWork
{
    /// <summary>Schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "shipping";

    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) => domainEvent switch
    {
        // E-shop krok 28C: V1 i V2 s číslem balíku (expand → contract); druhá zpráva má odvozené ID – inbox by stejné zahodil.
        ShipmentCreated e =>
        [
            OutboxMessageFactory.Create(new ShipmentCreatedV1(
                e.EventId, e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.Carrier!, e.Shipment.TrackingNumber!)),
            OutboxMessageFactory.Create(new ShipmentCreatedV2(
                MessageIds.Derive(e.EventId, nameof(ShipmentCreatedV2)), e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.ParcelNumber,
                e.Shipment.Carrier!, e.Shipment.TrackingNumber!))
        ],
        ShipmentFailed e =>
        [
            OutboxMessageFactory.Create(new ShipmentFailedV1(
                e.EventId, e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.FailureReason!)),
            OutboxMessageFactory.Create(new ShipmentFailedV2(
                MessageIds.Derive(e.EventId, nameof(ShipmentFailedV2)), e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.ParcelNumber,
                e.Shipment.FailureReason!))
        ],
        ShipmentDelivered e => [OutboxMessageFactory.Create(new ShipmentDeliveredV1(   // e-shop krok 26C
            e.EventId, e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.TrackingNumber!, e.OccurredAtUtc))],
        ShipmentStatusChanged e => [OutboxMessageFactory.Create(new ShipmentStatusChangedV1(   // vzhled krok F3b
            e.EventId, e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.TrackingNumber!, e.Status.ToString(), e.OccurredAtUtc))],
        ShipmentReturned e => [OutboxMessageFactory.Create(new ShipmentReturnedV1(     // e-shop krok 26D
            e.EventId, e.OccurredAtUtc, e.Shipment.OrderId, e.Shipment.TrackingNumber!, e.OccurredAtUtc))],
        _ => throw new InvalidOperationException(
            $"Událost {domainEvent.GetType().Name} nemá převod do outboxu. Doplňte ji do {nameof(ShippingDbContext)}.")
    };

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShippingDbContext).Assembly,
            type => type.Namespace == typeof(ShipmentConfiguration).Namespace);
}
