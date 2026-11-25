using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Review #185: zásoba nesmí být záporná ani při chybě v kódu nebo souběhu se starší verzí – poslední pojistka v DB.
        builder.ToTable("Products", t => t.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "\"StockQuantity\" >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.ImageFileName).HasMaxLength(100);   // e-shop krok 2B: jen jméno souboru, ne obrázek
        builder.Property(p => p.Price).HasPrecision(18, 2);

        // Review #185: souběh naskladnění (API) a rezervace (consumer) = ConcurrencyConflictException (zpráva se zopakuje, API 409).
        builder.Property(p => p.Version).IsConcurrencyToken();

        // E-shop krok 2: kategorie – smazat jde jen prázdnou (Restrict, hlídá i CategoryService).
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // Pásmo dostupnosti se počítá ze zásoby, ukládá se jen čas jeho poslední změny.
        builder.Ignore(p => p.Availability);

        // E-shop krok 28A: zásoba po skladech – vlastní tabulka (produkt × sklad), načítá se vždy s produktem.
        // StockQuantity zůstává = součet (starší verze skladu při postupném nasazení mění jen ho – viz Product.StockIn).
        builder.OwnsMany(p => p.Stocks, stock =>
        {
            stock.ToTable("WarehouseStocks",
                t => t.HasCheckConstraint("CK_WarehouseStocks_Quantity_NonNegative", "\"Quantity\" >= 0"));
            stock.WithOwner().HasForeignKey("ProductId");
            stock.HasKey("ProductId", nameof(WarehouseStock.WarehouseId));
            stock.HasOne<Warehouse>().WithMany().HasForeignKey(s => s.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(p => p.Stocks).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Doménové události se neukládají jako sloupce – InventoryDbContext je převede na zprávy v outboxu.
        builder.Ignore(p => p.DomainEvents);
    }
}
