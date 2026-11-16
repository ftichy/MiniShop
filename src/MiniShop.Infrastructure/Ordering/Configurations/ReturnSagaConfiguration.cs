using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Infrastructure.Ordering.Configurations;

/// <summary>
/// E-shop krok 12: tabulka ReturnSagas – „deník“ vrácení zboží. Krok 22: klíč objednávka + číslo vratky
/// (0 = celá objednávka – i všechny starší ságy), k objednávce tak může běžet víc vratek po sobě.
/// </summary>
internal sealed class ReturnSagaConfiguration : IEntityTypeConfiguration<ReturnSaga>
{
    public void Configure(EntityTypeBuilder<ReturnSaga> builder)
    {
        builder.ToTable("ReturnSagas");
        builder.HasKey(s => new { s.OrderId, s.ReturnNumber });
        builder.Ignore(s => s.IsPartial);
        builder.Property(s => s.OrderId).ValueGeneratedNever();   // ID přebírá z objednávky
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.RejectionReason).HasMaxLength(500);

        // Admin převezme zboží ve chvíli, kdy vyprší termín: druhý zápis narazí na verzi a zpracuje se znovu.
        builder.Property(s => s.Version).IsConcurrencyToken();


        // Timeout worker hledá: WHERE State = 'AwaitingGoods' AND GoodsDeadlineUtc <= now.
        builder.HasIndex(s => new { s.State, s.GoodsDeadlineUtc });
    }
}
