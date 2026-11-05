using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Infrastructure.Ordering.Configurations;

internal sealed class OrderSagaConfiguration : IEntityTypeConfiguration<OrderSaga>
{
    public void Configure(EntityTypeBuilder<OrderSaga> builder)
    {
        builder.ToTable("OrderSagas");
        builder.HasKey(s => s.OrderId);
        builder.Property(s => s.OrderId).ValueGeneratedNever();   // ID přebírá z objednávky
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.RejectionReason).HasMaxLength(500);

        // UPDATE … WHERE Version = původní: když ságu mezitím změnil někdo jiný (timeout vs. odpověď skladu),
        // EF vyhodí DbUpdateConcurrencyException a zpráva se zpracuje znovu nad čerstvým stavem.
        builder.Property(s => s.Version).IsConcurrencyToken();


        // Timeout worker hledá: WHERE State = 'AwaitingReservation' AND ReservationDeadlineUtc <= now.
        builder.HasIndex(s => new { s.State, s.ReservationDeadlineUtc });

        // E-shop krok 8: termín platby kartou (null = bez platby).
        builder.HasIndex(s => new { s.State, s.PaymentDeadlineUtc });
    }
}
