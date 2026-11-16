using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Payments;

namespace MiniShop.Infrastructure.Payments.Configurations;

/// <summary>E-shop krok 22: vrácení peněz z platby – stejné číslo vratky u jedné platby jen jednou.</summary>
internal sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.HasIndex(r => new { r.PaymentId, r.ReturnNumber }).IsUnique();
    }
}
