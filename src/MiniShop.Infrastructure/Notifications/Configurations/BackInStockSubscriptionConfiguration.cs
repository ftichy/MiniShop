using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications.Configurations;

internal sealed class BackInStockSubscriptionConfiguration : IEntityTypeConfiguration<BackInStockSubscription>
{
    public void Configure(EntityTypeBuilder<BackInStockSubscription> builder)
    {
        builder.ToTable("BackInStockSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CustomerId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.ProductName).IsRequired().HasMaxLength(200);

        // Jeden lísteček na zákazníka a produkt; i souběh dvou kliknutí skončí konfliktem, ne dvěma e-maily.
        builder.HasIndex(s => new { s.CustomerId, s.ProductId }).IsUnique();
        builder.HasIndex(s => s.ProductId);
    }
}
