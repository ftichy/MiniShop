using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Journeys;

namespace MiniShop.Infrastructure.Journeys.Configurations;

internal sealed class JourneyEventConfiguration : IEntityTypeConfiguration<JourneyEvent>
{
    public void Configure(EntityTypeBuilder<JourneyEvent> builder)
    {
        builder.ToTable("JourneyEvents");
        builder.HasKey(e => e.Id);

        // Idempotence: stejnou událost z prohlížeče (opakovaná dávka) databáze podruhé nepřijme.
        builder.HasIndex(e => e.EventId).IsUnique();
        builder.HasIndex(e => new { e.JourneyId, e.OffsetMs });

        // OrderId záměrně bez cizího klíče: objednávky jsou v jiné databázi (ordering.db) a Journeys je nezná.
        builder.HasIndex(e => e.OrderId);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Target).HasMaxLength(JourneyEvent.MaxTextLength);
        builder.Property(e => e.Label).HasMaxLength(JourneyEvent.MaxTextLength);
        builder.Property(e => e.Value).HasMaxLength(JourneyEvent.MaxTextLength);
        builder.Property(e => e.ValueText).HasMaxLength(JourneyEvent.MaxTextLength);
        builder.Property(e => e.RecordedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
