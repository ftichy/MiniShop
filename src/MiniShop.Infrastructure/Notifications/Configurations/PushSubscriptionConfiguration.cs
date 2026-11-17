using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CustomerId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Endpoint).IsRequired().HasMaxLength(PushSubscription.MaxEndpointLength);
        builder.Property(s => s.P256dh).IsRequired().HasMaxLength(PushSubscription.MaxKeyLength);
        builder.Property(s => s.Auth).IsRequired().HasMaxLength(PushSubscription.MaxKeyLength);
        builder.Property(s => s.DeviceLabel).IsRequired().HasMaxLength(PushSubscription.MaxDeviceLabelLength);

        // Jedno zařízení = jeden řádek (i souběh dvou kliknutí skončí konfliktem, ne dvěma oznámeními).
        builder.HasIndex(s => s.Endpoint).IsUnique();
        builder.HasIndex(s => s.CustomerId);
    }
}
