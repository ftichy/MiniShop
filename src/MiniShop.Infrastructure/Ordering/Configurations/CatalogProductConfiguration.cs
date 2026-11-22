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


        // E-shop krok 2: výloha.
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.CategoryName).HasMaxLength(50);
        builder.Property(p => p.ImageFileName).HasMaxLength(200);
        builder.Property(p => p.Availability).HasConversion<string>().HasMaxLength(20);   // text – čitelné v DB
        builder.Property(p => p.SearchText).IsRequired().HasMaxLength(800);

        // E-shop krok 21: index trojic písmen (pg_trgm) – zrychlí LIKE '%slovo%' i hledání podobných slov.
        builder.HasIndex(p => p.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.Ignore(p => p.IsOrderable);
        builder.Property(p => p.RatingAverage).HasPrecision(3, 1);   // e-shop krok 14: hodnocení z modulu Reviews

        // E-shop krok 27B: „Zákazníci také koupili“ – pole integer[] (pořadí = význam). Výchozí prázdné pole v DB:
        // starší verze Webu při postupném nasazení sloupec nezná a při vložení produktu ho nevyplní.
        builder.Ignore(p => p.RecommendedProductIds);
        builder.Property<List<int>>("_recommendedProductIds").HasColumnName("RecommendedProductIds")
            .IsRequired().HasDefaultValueSql("'{}'::integer[]");
    }
}
