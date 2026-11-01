using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniShop.Infrastructure.Journeys;

/// <summary>
/// E-shop krok 1: jen pro nástroj <c>dotnet ef</c> – vytvoří JourneysDbContext bez spuštění Webu.
/// Aplikace ho nepoužívá. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context JourneysDbContext --output-dir Journeys/Migrations</code>
/// </summary>
internal sealed class JourneysDesignTimeDbContextFactory : IDesignTimeDbContextFactory<JourneysDbContext>
{
    public JourneysDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<JourneysDbContext>().UseSqlite("Data Source=journeys.db").Options);
}
