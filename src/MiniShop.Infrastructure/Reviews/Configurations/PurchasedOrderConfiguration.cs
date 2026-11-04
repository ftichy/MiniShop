using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Reviews;

namespace MiniShop.Infrastructure.Reviews.Configurations;

internal sealed class PurchasedOrderConfiguration : IEntityTypeConfiguration<PurchasedOrder>
{
    public void Configure(EntityTypeBuilder<PurchasedOrder> builder)
    {
        builder.ToTable("PurchasedOrders");

        // Číslo objednávky z Orderingu = klíč (ne autoincrement) – bez cizího klíče, Reviews Ordering nezná.
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).ValueGeneratedNever();
        builder.Property(o => o.CustomerId).HasMaxLength(64);
        builder.HasIndex(o => o.CustomerId);   // „smí hodnotit?“ a zapomenutí zákazníka
        builder.Property(o => o.CustomerName).HasMaxLength(100);

        builder.OwnsMany(o => o.Products, product =>
        {
            product.ToTable("PurchasedProducts");
            product.WithOwner().HasForeignKey("OrderId");
            product.Property<int>("Id");
            product.HasKey("Id");
            product.HasIndex(p => p.ProductId);
        });
        builder.Navigation(o => o.Products).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
