using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Reporting;

namespace MiniShop.Infrastructure.Reporting.Configurations;

/// <summary>E-shop krok 27A: naposledy odeslaná doporučení – ID produktu z katalogu = klíč, seznam ID jako pole integer[].</summary>
internal sealed class ProductRecommendationConfiguration : IEntityTypeConfiguration<ProductRecommendation>
{
    public void Configure(EntityTypeBuilder<ProductRecommendation> builder)
    {
        builder.ToTable("ProductRecommendations");
        builder.HasKey(r => r.ProductId);
        builder.Property(r => r.ProductId).ValueGeneratedNever();

        // Pořadí v seznamu je význam (nejčastější první) – PostgreSQL pole pořadí drží.
        builder.Ignore(r => r.RecommendedProductIds);
        builder.Property<List<int>>("_recommendedProductIds").HasColumnName("RecommendedProductIds").IsRequired();
    }
}
