using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Databáze modulu Accounts – users.db (účty krok 1): tabulky Identity (AspNetUsers, AspNetRoles, AspNetUserRoles…).
/// </summary>
/// <remarks>
/// Jako Journeys nedědí z ModuleDbContext: účty zatím neposílají ani nepřijímají zprávy, outbox ani inbox nepotřebují.
/// Tabulky rolí vznikají už teď, i když role přijdou až v kroku 2 – nebude potřeba mazat databázi.
/// </remarks>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>()
            .Property(user => user.DisplayName)
            .HasMaxLength(ApplicationUser.DisplayNameMaxLength)
            .IsRequired();
    }
}
