using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// E-shop krok 7: jen pro nástroj <c>dotnet ef</c>. Nová migrace (z kořene repozitáře):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context NotificationsDbContext --output-dir Notifications/Migrations</code>
/// </summary>
internal sealed class NotificationsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseSqlite("Data Source=notifications.db").Options);
}
