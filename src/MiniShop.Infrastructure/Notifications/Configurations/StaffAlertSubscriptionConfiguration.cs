using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications.Configurations;

internal sealed class StaffAlertSubscriptionConfiguration : IEntityTypeConfiguration<StaffAlertSubscription>
{
    public void Configure(EntityTypeBuilder<StaffAlertSubscription> builder)
    {
        builder.ToTable("StaffAlertSubscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(40);   // text – nové typy jen na konec

        // Jedna volba na účet a typ (dvojklik = konflikt, ne dva řádky).
        builder.HasIndex(s => new { s.UserId, s.Kind }).IsUnique();
        builder.HasIndex(s => s.Kind);
    }
}
