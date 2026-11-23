using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory.Configurations;

/// <summary>E-shop krok 28A: sklady – zakládá je migrace (HasData), ID se negenerují (Praha = výchozí 1).</summary>
internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Code).IsRequired().HasMaxLength(10);
        builder.Property(w => w.Name).IsRequired().HasMaxLength(50);
        builder.HasIndex(w => w.Code).IsUnique();

        builder.HasData(
            new Warehouse(Warehouse.DefaultId, "PRG", "Praha"),
            new Warehouse(Warehouse.BrnoId, "BRQ", "Brno"));
    }
}
