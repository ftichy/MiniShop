using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Payments;

namespace MiniShop.Infrastructure.Payments.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // Jedna objednávka = jedna platba – i při souběhu dvou stejných příkazů (unikátní index).
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.Property(p => p.CustomerId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ExternalId).HasMaxLength(100);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(500);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Ignore(p => p.DomainEvents);

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(p => p.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(p => p.ExpiresAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        builder.Property(p => p.CompletedAtUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
    }
}
