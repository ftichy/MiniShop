using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Shipping;

namespace MiniShop.Infrastructure.Shipping.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Jeden příkaz = jedna zásilka; vytvořená zásilka nejvýš jedna na objednávku (odmítnutých může být víc).
        builder.HasIndex(s => s.CommandId).IsUnique();
        builder.HasIndex(s => s.OrderId).IsUnique().HasFilter("\"Status\" = 'Created'");
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Carrier).HasMaxLength(Shipment.CarrierMaxLength);
        builder.Property(s => s.TrackingNumber).HasMaxLength(Shipment.TrackingNumberMaxLength);
        builder.Property(s => s.FailureReason).HasMaxLength(Shipment.FailureMaxLength);
        builder.Ignore(s => s.DomainEvents);

        // E-shop krok 26C: webhook hledá zásilku podle čísla od dopravce; historie stavů; souběh dvou webhooků.
        builder.HasIndex(s => s.TrackingNumber);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Ignore(s => s.IsOnTheWay);
        builder.HasMany(s => s.Tracking).WithOne().HasForeignKey(t => t.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Tracking).HasField("_tracking").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary>E-shop krok 26C: historie stavů od dopravce – stejné pořadí (<c>Sequence</c>) u zásilky jen jednou.</summary>
internal sealed class ShipmentTrackingEventConfiguration : IEntityTypeConfiguration<ShipmentTrackingEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentTrackingEvent> builder)
    {
        builder.ToTable("ShipmentTrackingEvents");
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.ShipmentId, t.Sequence }).IsUnique();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
    }
}
