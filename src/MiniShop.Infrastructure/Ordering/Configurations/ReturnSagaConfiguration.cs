using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Infrastructure.Ordering.Configurations;

/// <summary>E-shop krok 12: tabulka ReturnSagas – „deník“ vrácení zboží (jedna vratka na objednávku).</summary>
internal sealed class ReturnSagaConfiguration : IEntityTypeConfiguration<ReturnSaga>
{
    public void Configure(EntityTypeBuilder<ReturnSaga> builder)
    {
        builder.ToTable("ReturnSagas");
        builder.HasKey(s => s.OrderId);
        builder.Property(s => s.OrderId).ValueGeneratedNever();   // ID přebírá z objednávky
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.RejectionReason).HasMaxLength(500);

        // Admin převezme zboží ve chvíli, kdy vyprší termín: druhý zápis narazí na verzi a zpracuje se znovu.
        builder.Property(s => s.Version).IsConcurrencyToken();

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(s => s.StartedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(s => s.UpdatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(s => s.GoodsDeadlineUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        // Timeout worker hledá: WHERE State = 'AwaitingGoods' AND GoodsDeadlineUtc <= now.
        builder.HasIndex(s => new { s.State, s.GoodsDeadlineUtc });
    }
}
