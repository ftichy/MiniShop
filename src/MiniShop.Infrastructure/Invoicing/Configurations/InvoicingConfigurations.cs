using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Invoicing;

namespace MiniShop.Infrastructure.Invoicing.Configurations;

internal sealed class BillableOrderConfiguration : IEntityTypeConfiguration<BillableOrder>
{
    public void Configure(EntityTypeBuilder<BillableOrder> builder)
    {
        builder.ToTable("BillableOrders");

        // Číslo objednávky z Orderingu = klíč (ne autoincrement) – bez cizího klíče, Invoicing Ordering nezná.
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).ValueGeneratedNever();
        builder.Property(o => o.CustomerId).HasMaxLength(64);
        builder.HasIndex(o => o.CustomerId);   // zapomenutí zákazníka
        builder.Property(o => o.CustomerName).HasMaxLength(100);
        builder.Property(o => o.DeliveryAddress).HasMaxLength(300);
        builder.Property(o => o.ShippingMethod).HasMaxLength(50);
        builder.Property(o => o.PaymentNote).HasMaxLength(100);
        builder.Property(o => o.ShippingPrice).HasPrecision(18, 2);
        builder.Property(o => o.Total).HasPrecision(18, 2);

        builder.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("BillableOrderLines");
            line.WithOwner().HasForeignKey("OrderId");
            line.Property<int>("Id");
            line.HasKey("Id");
            ConfigureLine(line);
        });
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    internal static void ConfigureLine<TOwner>(OwnedNavigationBuilder<TOwner, InvoiceLine> line) where TOwner : class
    {
        line.Property(l => l.ProductName).HasMaxLength(200);
        line.Property(l => l.UnitPrice).HasPrecision(18, 2);
        line.Property(l => l.Discount).HasPrecision(18, 2);
        line.Property(l => l.LineTotal).HasPrecision(18, 2);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);

        // Jedna faktura na objednávku a každé číslo jen jednou – poslední pojistka i při souběhu dvou kopií Webu.
        builder.HasIndex(i => i.OrderId).IsUnique();
        builder.Property(i => i.Number).HasMaxLength(20);
        builder.HasIndex(i => i.Number).IsUnique();

        builder.Property(i => i.CustomerId).HasMaxLength(64);
        builder.HasIndex(i => i.CustomerId);
        builder.Property(i => i.CustomerName).HasMaxLength(100);
        builder.Property(i => i.DeliveryAddress).HasMaxLength(300);
        builder.Property(i => i.ShippingMethod).HasMaxLength(50);
        builder.Property(i => i.PaymentNote).HasMaxLength(100);
        builder.Property(i => i.ShippingPrice).HasPrecision(18, 2);
        builder.Property(i => i.Total).HasPrecision(18, 2);

        builder.OwnsMany(i => i.Lines, line =>
        {
            line.ToTable("InvoiceLines");
            line.WithOwner().HasForeignKey("InvoiceId");
            line.Property<int>("Id");
            line.HasKey("Id");
            BillableOrderConfiguration.ConfigureLine(line);
        });
        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvoiceNumberSeriesConfiguration : IEntityTypeConfiguration<InvoiceNumberSeries>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSeries> builder)
    {
        builder.ToTable("InvoiceNumberSeries");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();

        // Dvě faktury naráz: UPDATE … WHERE Version = původní – druhá transakce se zruší celá (i s fakturou)
        // a consumer zprávu zopakuje s dalším číslem. Řada tak nemá díry ani dvě stejná čísla.
        builder.Property(s => s.Version).IsConcurrencyToken();
    }
}
