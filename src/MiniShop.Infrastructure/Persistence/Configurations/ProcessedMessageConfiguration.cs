using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Infrastructure.Inbox;

namespace MiniShop.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");

        // Složený klíč: stejnou událost smí zpracovat víc handlerů, ale každý jen jednou.
        // Kdyby se dva pokusy přece jen potkaly, databáze druhý zápis odmítne.
        builder.HasKey(m => new { m.EventId, m.Handler });
        builder.Property(m => m.Handler).HasMaxLength(300);

        // SQLite neukládá DateTimeKind – při načtení čas označíme jako UTC.
        builder.Property(m => m.ProcessedAtUtc)
            .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    }
}
