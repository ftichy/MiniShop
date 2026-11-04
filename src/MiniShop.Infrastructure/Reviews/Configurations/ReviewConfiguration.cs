using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Reviews;

namespace MiniShop.Infrastructure.Reviews.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CustomerId).IsRequired().HasMaxLength(64);
        builder.Property(r => r.AuthorName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Text).IsRequired().HasMaxLength(Review.TextMaxLength);
        builder.HasIndex(r => r.ProductId);

        // Jedno hodnocení na zákazníka a produkt – i v DB (dvojklik, dvě záložky). Anonymizovaná hodnocení (CustomerId '')
        // se nepočítají – po smazání účtů jich u produktu může být víc.
        builder.HasIndex(r => new { r.ProductId, r.CustomerId }).IsUnique().HasFilter("\"CustomerId\" <> ''");

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(r => r.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(r => r.UpdatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
