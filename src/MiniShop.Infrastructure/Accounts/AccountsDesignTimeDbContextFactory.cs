using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniShop.Infrastructure.Accounts;

/// <summary>
/// Účty krok 6: jen pro nástroj <c>dotnet ef</c> – vytvoří AccountsDbContext bez spuštění Webu.
/// Aplikace ho nepoužívá. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context AccountsDbContext --output-dir Accounts/Migrations</code>
/// </summary>
internal sealed class AccountsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite("Data Source=users.db").Options);
}
