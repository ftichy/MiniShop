using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniShop.Infrastructure.Inventory;

/// <summary>
/// E-shop krok 1: jen pro nástroj <c>dotnet ef</c> – vytvoří InventoryDbContext bez spuštění Inventory.Api.
/// Aplikace ho nepoužívá. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context InventoryDbContext --output-dir Inventory/Migrations</code>
/// </summary>
internal sealed class InventoryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite("Data Source=inventory.db").Options);
}
