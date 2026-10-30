using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.ImageFileName).HasMaxLength(100);   // e-shop krok 2B: jen jméno souboru, ne obrázek
        builder.Property(p => p.Price).HasPrecision(18, 2);

        // E-shop krok 2: kategorie – smazat jde jen prázdnou (Restrict, hlídá i CategoryService).
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // Pásmo dostupnosti se počítá ze zásoby, ukládá se jen čas jeho poslední změny.
        builder.Ignore(p => p.Availability);
        builder.Property(p => p.AvailabilityChangedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Doménové události se neukládají jako sloupce – InventoryDbContext je převede na zprávy v outboxu.
        builder.Ignore(p => p.DomainEvents);
    }
}
