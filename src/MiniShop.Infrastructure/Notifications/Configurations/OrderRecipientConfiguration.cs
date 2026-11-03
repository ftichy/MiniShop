using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications.Configurations;

internal sealed class OrderRecipientConfiguration : IEntityTypeConfiguration<OrderRecipient>
{
    public void Configure(EntityTypeBuilder<OrderRecipient> builder)
    {
        builder.ToTable("OrderRecipients");

        // Číslo objednávky z Orderingu = klíč (ne autoincrement) – bez cizího klíče, Notifications Ordering nezná.
        builder.HasKey(r => r.OrderId);
        builder.Property(r => r.OrderId).ValueGeneratedNever();

        // Po smazání účtu se hledá podle zákazníka (CustomerAnonymizedV1).
        builder.Property(r => r.CustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(r => r.CustomerId);
        builder.Property(r => r.CustomerName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.CustomerEmail).IsRequired().HasMaxLength(200);
        builder.Property(r => r.ShippingMethod).IsRequired().HasMaxLength(20);
        builder.Property(r => r.ShippingPrice).HasPrecision(18, 2);
        builder.Property(r => r.DeliveryAddress).HasMaxLength(300);
        builder.Property(r => r.TotalPrice).HasPrecision(18, 2);
        builder.Ignore(r => r.IsPickup);

        // Položky jen pro souhrn v e-mailu – vlastní tabulka, smažou se s příjemcem.
        builder.OwnsMany(r => r.Items, item =>
        {
            item.ToTable("OrderRecipientItems");
            item.WithOwner().HasForeignKey("OrderId");
            item.HasKey(i => i.Id);
            item.Property(i => i.ProductName).IsRequired().HasMaxLength(100);
            item.Property(i => i.LineTotal).HasPrecision(18, 2);
        });
        builder.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
