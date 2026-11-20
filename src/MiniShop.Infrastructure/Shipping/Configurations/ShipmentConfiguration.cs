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
    }
}
