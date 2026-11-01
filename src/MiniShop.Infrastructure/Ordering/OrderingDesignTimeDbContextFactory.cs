using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// E-shop krok 1: jen pro nástroj <c>dotnet ef</c> – vytvoří OrderingDbContext bez spuštění Webu.
/// Aplikace ho nepoužívá. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context OrderingDbContext --output-dir Ordering/Migrations</code>
/// </summary>
internal sealed class OrderingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlite("Data Source=ordering.db").Options);
}
