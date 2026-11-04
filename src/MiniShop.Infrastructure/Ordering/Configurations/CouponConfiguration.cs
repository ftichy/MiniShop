using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders.Coupons;

namespace MiniShop.Infrastructure.Ordering.Configurations;

/// <summary>E-shop krok 13: tabulka Coupons – slevové kódy.</summary>
internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons");
        builder.HasKey(c => c.Id);

        // Kód je ukládaný velkými písmeny – unikátní index = dva stejné kódy nevzniknou ani souběžně.
        builder.Property(c => c.Code).IsRequired().HasMaxLength(Coupon.CodeMaxLength);
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Value).HasPrecision(18, 2);
        builder.Property(c => c.MinItemsTotal).HasPrecision(18, 2);
        builder.Ignore(c => c.IsExhausted);

        // Poslední použití ze dvou pokladen naráz: UPDATE … WHERE Version = původní – druhá transakce se zruší celá.
        builder.Property(c => c.Version).IsConcurrencyToken();

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(c => c.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(c => c.ValidUntilUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
    }
}
