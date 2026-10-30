using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering.Configurations;

internal sealed class CatalogProductConfiguration : IEntityTypeConfiguration<CatalogProduct>
{
    public void Configure(EntityTypeBuilder<CatalogProduct> builder)
    {
        builder.ToTable("CatalogProducts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();   // ID přebírá ze skladu
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Price).HasPrecision(18, 2);

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(p => p.ChangedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // E-shop krok 2: výloha.
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.CategoryName).HasMaxLength(50);
        builder.Property(p => p.ImageFileName).HasMaxLength(200);
        builder.Property(p => p.Availability).HasConversion<string>().HasMaxLength(20);   // text – čitelné v DB
        builder.Property(p => p.AvailabilityChangedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(p => p.SearchText).IsRequired().HasMaxLength(800);
        builder.Ignore(p => p.IsOrderable);
    }
}
