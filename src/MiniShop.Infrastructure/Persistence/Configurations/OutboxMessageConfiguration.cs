using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).IsRequired().HasMaxLength(200);

        // Text místo čísla – v DB Browseru je hned vidět "Domain" / "Integration".
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.Error).HasMaxLength(2000);
        builder.Property(m => m.TraceParent).HasMaxLength(64);   // krok 11, W3C traceparent má 55 znaků


        // EDA krok 13: backoff – selhaná zpráva se zkusí až po tomhle čase.

        // Worker (krok 5) hledá nezpracované zprávy: WHERE ProcessedAtUtc IS NULL; úklid (EDA krok 13) staré zpracované.
        builder.HasIndex(m => m.ProcessedAtUtc);
    }
}
