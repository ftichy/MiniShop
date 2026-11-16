using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory.Configurations;

/// <summary>E-shop krok 22: naskladněné vratky – jedna vratka (objednávka + pořadí) jen jednou.</summary>
internal sealed class StockReturnConfiguration : IEntityTypeConfiguration<StockReturn>
{
    public void Configure(EntityTypeBuilder<StockReturn> builder)
    {
        builder.ToTable("StockReturns");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.OrderId, r.ReturnNumber }).IsUnique();
    }
}
