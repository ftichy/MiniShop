using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.IsActive);

        // OrderId záměrně bez cizího klíče na Orders: sklad objednávky nezná (příprava na samostatnou službu, krok 10).
        builder.HasIndex(r => r.OrderId);

        // E-shop krok 28A: sklad původu. Výchozí hodnota v DB = Praha – starší verze kódu sloupec nezná a při postupném nasazení
        // zakládá rezervace bez něj (bez cizího klíče – sklady jsou pevné a rezervace se nemažou).
        builder.Property(r => r.WarehouseId).HasDefaultValue(Warehouse.DefaultId);

        builder.Ignore(r => r.CanBeReturned);
        builder.Ignore(r => r.ReturnableQuantity);
        // ReturnedQuantity (e-shop krok 22): migrace přidá sloupec s výchozí 0 – starší verze kódu ho nezná a nevadí to.
    }
}
