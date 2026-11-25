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
        builder.Property(o => o.ReturnReason).HasMaxLength(Order.ReturnReasonMaxLength);   // e-shop krok 12
        builder.Property(o => o.ReturnRejectionReason).HasMaxLength(500);
        builder.Property(o => o.CouponCode).HasMaxLength(MiniShop.Domain.Orders.Coupons.Coupon.CodeMaxLength);   // e-shop krok 13
        builder.Property(o => o.CouponDiscount).HasPrecision(18, 2);
        builder.HasIndex(o => new { o.CustomerId, o.CouponId });   // „jeden zákazník jednou“
        builder.Ignore(o => o.DeliveredAtUtc);

        builder.Ignore(o => o.TotalPrice);
        builder.Ignore(o => o.ItemsTotal);
        builder.Ignore(o => o.DomainEvents);

        // E-shop krok 4: idempotence pokladny – stejné odeslání nesmí dát dvě objednávky ani při souběhu.
        // Starší objednávky mají NULL (PostgreSQL v unikátním indexu NULL nepočítá jako shodu).
        builder.HasIndex(o => o.CheckoutId).IsUnique();
        builder.Property(o => o.ShippingMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ShippingPrice).HasPrecision(18, 2);
        builder.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(20);   // e-shop krok 8

        // E-shop krok 6: expedice. Version = concurrency token – UPDATE … WHERE Version = původní; když objednávku
        // mezitím změnil někdo jiný (admin odeslal × zákazník zrušil), EF vyhodí výjimku a ModuleDbContext ji převede
        // na ConcurrencyConflictException (409 / hláška na stránce).
        builder.Property(o => o.Carrier).HasMaxLength(50);
        builder.Property(o => o.TrackingNumber).HasMaxLength(50);
        builder.Property(o => o.ShipmentError).HasMaxLength(Order.ShipmentErrorMaxLength);   // e-shop krok 26B
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

        // E-shop krok 22: vratky po částech – vlastní tabulka, načítají se vždy (pravidla vrácení, IsActive; je jich málo).
        builder.HasMany(o => o.Returns)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Returns).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Ignore(o => o.OpenReturn);

        // E-shop krok 28B: balíky podle skladů – vlastní tabulka, načítají se vždy (Expedice, detail; jsou 1–2).
        builder.HasMany(o => o.Parcels)
            .WithOne()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Parcels).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

/// <summary>E-shop krok 28B: balík objednávky – unikátní objednávka + číslo, položky ve vlastní tabulce.</summary>
internal sealed class OrderParcelConfiguration : IEntityTypeConfiguration<OrderParcel>
{
    public void Configure(EntityTypeBuilder<OrderParcel> builder)
    {
        builder.ToTable("OrderParcels");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.OrderId, p.Number }).IsUnique();
        builder.Property(p => p.WarehouseCode).IsRequired().HasMaxLength(10);
        builder.Property(p => p.WarehouseName).IsRequired().HasMaxLength(50);
        // E-shop krok 28C: zásilka balíku u dopravce (doručení a vrácení hlásí dopravce číslem zásilky).
        builder.Property(p => p.Carrier).HasMaxLength(50);
        builder.Property(p => p.TrackingNumber).HasMaxLength(50);
        builder.Property(p => p.ShipmentError).HasMaxLength(Order.ShipmentErrorMaxLength);
        builder.Ignore(p => p.IsAwaitingShipment);
        builder.Ignore(p => p.IsShipped);
        builder.Ignore(p => p.IsFinished);

        builder.OwnsMany(p => p.Lines, line =>
        {
            line.ToTable("OrderParcelLines");
            line.WithOwner().HasForeignKey("OrderParcelId");
            line.Property<int>("Id");
            line.HasKey("Id");
        });
        builder.Navigation(p => p.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>E-shop krok 22: vratka po částech – unikátní objednávka + číslo, řádky ve vlastní tabulce.</summary>
internal sealed class OrderReturnConfiguration : IEntityTypeConfiguration<OrderReturn>
{
    public void Configure(EntityTypeBuilder<OrderReturn> builder)
    {
        builder.ToTable("OrderReturns");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.OrderId, r.Number }).IsUnique();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasMaxLength(OrderReturn.ReasonMaxLength);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Ignore(r => r.Amount);
        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.CountsAsReturned);

        builder.OwnsMany(r => r.Lines, line =>
        {
            line.ToTable("OrderReturnLines");
            line.WithOwner().HasForeignKey("OrderReturnId");
            line.Property<int>("Id");
            line.HasKey("Id");
            line.Property(l => l.ProductName).IsRequired().HasMaxLength(100);
            line.Property(l => l.UnitPrice).HasPrecision(18, 2);
            line.Property(l => l.Discount).HasPrecision(18, 2);
            line.Ignore(l => l.LineTotal);
        });
        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
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
