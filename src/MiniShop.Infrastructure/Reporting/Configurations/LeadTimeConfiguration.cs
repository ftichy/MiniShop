using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Reporting;

namespace MiniShop.Infrastructure.Reporting.Configurations;

/// <summary>Proud B2: časy objednávek pro přehled Rychlost vyřízení – klíč = číslo objednávky z Orderingu.</summary>
internal sealed class OrderLeadTimeConfiguration : IEntityTypeConfiguration<OrderLeadTime>
{
    public void Configure(EntityTypeBuilder<OrderLeadTime> builder)
    {
        builder.ToTable("OrderLeadTimes");
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).ValueGeneratedNever();
        builder.HasIndex(o => o.ConfirmedAtUtc);
        builder.HasIndex(o => o.ShippedAtUtc);
        builder.HasIndex(o => o.DeliveredAtUtc);
        builder.Ignore(o => o.Confirmation);
        builder.Ignore(o => o.Expedition);
        builder.Ignore(o => o.Delivery);
    }
}

/// <summary>Proud B2: záložky čtenářů proudu (offset + generace) – <c>Version</c> hlídá souběh dávky a přestavění.</summary>
internal sealed class StreamCheckpointConfiguration : IEntityTypeConfiguration<StreamCheckpoint>
{
    public void Configure(EntityTypeBuilder<StreamCheckpoint> builder)
    {
        builder.ToTable("StreamCheckpoints");
        builder.HasKey(c => c.Name);
        builder.Property(c => c.Name).HasMaxLength(50);
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}
