using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        // Účty krok 3: vlastník. Index – seznam „moje objednávky“ filtruje podle něj.
        builder.Property(o => o.CustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(o => o.CustomerId);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(100);
        builder.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(o => o.CreatedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Ignore(o => o.TotalPrice);
        builder.Ignore(o => o.ItemsTotal);
        builder.Ignore(o => o.DomainEvents);

        // E-shop krok 4: idempotence pokladny – stejné odeslání nesmí dát dvě objednávky ani při souběhu.
        // Starší objednávky mají NULL (SQLite v unikátním indexu NULL nepočítá jako shodu).
        builder.HasIndex(o => o.CheckoutId).IsUnique();
        builder.Property(o => o.ShippingMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ShippingPrice).HasPrecision(18, 2);

        // E-shop krok 6: expedice. Version = concurrency token – UPDATE … WHERE Version = původní; když objednávku
        // mezitím změnil někdo jiný (admin odeslal × zákazník zrušil), EF vyhodí výjimku a ModuleDbContext ji převede
        // na ConcurrencyConflictException (409 / hláška na stránce).
        builder.Property(o => o.Carrier).HasMaxLength(50);
        builder.Property(o => o.TrackingNumber).HasMaxLength(50);
        builder.Property(o => o.Version).IsConcurrencyToken();

        // Hodnotový objekt = sloupce v tabulce Orders (žádná vlastní tabulka ani ID). Bez adresy = všechny NULL.
        builder.OwnsOne(o => o.DeliveryAddress, address =>
        {
            address.Property(a => a.Street).HasColumnName("DeliveryStreet").HasMaxLength(DeliveryAddress.MaxLength);
            address.Property(a => a.City).HasColumnName("DeliveryCity").HasMaxLength(DeliveryAddress.MaxLength);
            address.Property(a => a.PostalCode).HasColumnName("DeliveryPostalCode").HasMaxLength(5);
            address.Ignore(a => a.PostalCodeFormatted);
        });

        // Položky jsou uložené v privátním poli _items, EF k nim přistupuje přímo.
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // E-shop krok 5: historie stavů – vlastní tabulka, stejně jako položky přes privátní pole.
        builder.HasMany(o => o.StatusHistory)
            .WithOne()
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("OrderStatusChanges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Note).HasMaxLength(500);

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC (null = starší objednávka bez známého času).
        builder.Property(c => c.ChangedAtUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(100);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.Discount).HasPrecision(18, 2);
        builder.Ignore(i => i.LineTotal);

        // ProductId je záměrně bez cizího klíče: objednávka drží snímek produktu
        // a nezávisí na tom, zda produkt v katalogu ještě existuje.
        builder.HasIndex(i => i.ProductId);
    }
}
