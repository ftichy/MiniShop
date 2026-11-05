using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Databáze modulu Accounts – users.db (účty krok 1): tabulky Identity (AspNetUsers, AspNetRoles, AspNetUserRoles…).
/// </summary>
/// <remarks>
/// Jako Journeys nedědí z ModuleDbContext: účty zatím neposílají ani nepřijímají zprávy, outbox ani inbox nepotřebují.
/// Tabulky rolí vznikají už teď, i když role přijdou až v kroku 2 – nebude potřeba mazat databázi.
/// Účty krok 6: schéma se mění přes EF Core migrace (složka Migrations) – users.db se nemaže, jsou v ní účty.
/// </remarks>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "accounts";

    public DbSet<AccountAuditEntry> AuditEntries => Set<AccountAuditEntry>();

    // Škálování krok 1: v PostgreSQL časy jako UTC a tabulky ve schématu accounts.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseModuleSchema(this);
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>()
            .Property(user => user.DisplayName)
            .HasMaxLength(ApplicationUser.DisplayNameMaxLength)
            .IsRequired();

        modelBuilder.Entity<AccountAuditEntry>(audit =>
        {
            audit.ToTable("AccountAuditEntries");
            audit.HasKey(entry => entry.Id);
            audit.Property(entry => entry.ActorUserId).IsRequired().HasMaxLength(AccountAuditEntry.UserIdMaxLength);
            audit.Property(entry => entry.ActorEmail).IsRequired().HasMaxLength(AccountAuditEntry.EmailMaxLength);
            audit.Property(entry => entry.TargetUserId).IsRequired().HasMaxLength(AccountAuditEntry.UserIdMaxLength);
            audit.Property(entry => entry.TargetEmail).IsRequired().HasMaxLength(AccountAuditEntry.EmailMaxLength);
            audit.Property(entry => entry.Action).HasConversion<string>().HasMaxLength(30);
            // Detail uživatele čte jeho historii.
            audit.HasIndex(entry => entry.TargetUserId);
        });
    }
}
