using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Inventory;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Infrastructure.Notifications;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Payments;
using MiniShop.Infrastructure.Persistence;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Infrastructure.Reviews;

namespace MiniShop.Infrastructure;

/// <summary>
/// Kontexty modulů jen pro nástroj <c>dotnet ef</c> – připojení k serveru není potřeba, migrace se jen generují.
/// Aplikace je nepoužívá. Nová migrace (z kořene repozitáře, poprvé <c>dotnet tool restore</c>):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure --context OrderingDbContext --output-dir Ordering/Migrations</code>
/// </summary>
internal static class DesignTimeDatabase
{
    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext =>
        (DbContextOptions<TContext>)new DbContextOptionsBuilder<TContext>()
            .UseModuleDatabase(new ModuleDatabase("Host=localhost;Database=minishop"), schema)
            .Options;
}

internal sealed class OrderingDesignTimeFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<OrderingDbContext>(OrderingDbContext.Schema));
}

internal sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<InventoryDbContext>(InventoryDbContext.Schema));
}

internal sealed class JourneysDesignTimeFactory : IDesignTimeDbContextFactory<JourneysDbContext>
{
    public JourneysDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<JourneysDbContext>(JourneysDbContext.Schema));
}

internal sealed class NotificationsDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<NotificationsDbContext>(NotificationsDbContext.Schema));
}

internal sealed class PaymentsDesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<PaymentsDbContext>(PaymentsDbContext.Schema));
}

internal sealed class ReportingDesignTimeFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<ReportingDbContext>(ReportingDbContext.Schema));
}

internal sealed class ReviewsDesignTimeFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<ReviewsDbContext>(ReviewsDbContext.Schema));
}

internal sealed class AccountsDesignTimeFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) => new(DesignTimeDatabase.Options<AccountsDbContext>(AccountsDbContext.Schema));
}
