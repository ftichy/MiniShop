using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Infrastructure.Ordering.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(c => c.Id);
        // Jeden košík na zákazníka – pojistka i proti dvěma souběžným „prvním přidáním“.
        builder.Property(c => c.CustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(c => c.CustomerId).IsUnique();

        builder.Ignore(c => c.TotalQuantity);

        // E-shop krok 4: verze košíku jako concurrency token – UPDATE … WHERE CheckoutId = původní. Dvě souběžná
        // odeslání pokladny (nebo změny ze dvou záložek): druhé narazí a jeho transakce se celá zruší.
        builder.Property(c => c.CheckoutId).IsConcurrencyToken();
        builder.Property(c => c.CouponCode).HasMaxLength(MiniShop.Domain.Orders.Coupons.Coupon.CodeMaxLength);   // e-shop krok 13

        // Řádky jsou v privátním poli _lines, EF k nim přistupuje přímo.
        builder.HasMany(c => c.Lines)
            .WithOne()
            .HasForeignKey(l => l.CartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("CartLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.PriceWhenAdded).HasPrecision(18, 2);

        // Každý produkt v košíku jen jednou (invariant agregátu, tady i v DB).
        // ProductId bez cizího klíče na kopii katalogu – stejně jako u položky objednávky.
        builder.HasIndex(l => new { l.CartId, l.ProductId }).IsUnique();
    }
}
