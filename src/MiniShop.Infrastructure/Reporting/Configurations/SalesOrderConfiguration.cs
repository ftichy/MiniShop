using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Reporting;

namespace MiniShop.Infrastructure.Reporting.Configurations;

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrders");

        // Číslo objednávky z Orderingu = klíč (ne autoincrement) – bez cizího klíče, Reporting Ordering nezná.
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).ValueGeneratedNever();

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(o => o.PlacedAtUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        builder.HasIndex(o => o.PlacedAtUtc);   // přehled bere posledních 30 dní
        builder.Property(o => o.UpdatedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.TotalPrice).HasPrecision(18, 2);
        builder.Property(o => o.ShippingPrice).HasPrecision(18, 2);
        builder.Property(o => o.RefundedAmount).HasPrecision(18, 2);
        builder.Ignore(o => o.CountsAsRevenue);

        // Řádky jen pro „top produkty“ – vlastní tabulka, smažou se s objednávkou.
        builder.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("SalesOrderLines");
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<int>("Id");
            line.HasKey("Id");
            line.Property(l => l.ProductName).IsRequired().HasMaxLength(100);
            line.Property(l => l.LineTotal).HasPrecision(18, 2);
        });
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
