using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.IsActive);

        // OrderId záměrně bez cizího klíče na Orders: sklad objednávky nezná (příprava na samostatnou službu, krok 10).
        builder.HasIndex(r => r.OrderId);

        builder.Property(r => r.ReservedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(r => r.ReleasedAtUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
    }
}
