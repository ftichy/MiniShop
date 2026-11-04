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

namespace MiniShop.Infrastructure.PostgreSql;

/// <summary>
/// Škálování krok 1: kontexty modulů nad PostgreSQL – jen pro nástroj <c>dotnet ef</c> (připojení k serveru není potřeba,
/// migrace se jen generují). Továrny v MiniShop.Infrastructure vytvářejí tytéž kontexty nad SQLite. Nová migrace
/// (z kořene repozitáře, vždy pro OBA providery – nejdřív SQLite, pak PostgreSQL se stejným názvem):
/// <code>dotnet ef migrations add &lt;Nazev&gt; --project src/MiniShop.Infrastructure.PostgreSql --context OrderingDbContext --output-dir Ordering/Migrations --namespace MiniShop.Infrastructure.PostgreSql.Ordering.Migrations</code>
/// </summary>
internal static class PostgresDesignTime
{
    public static DbContextOptions<TContext> Options<TContext>(string schema) where TContext : DbContext =>
        (DbContextOptions<TContext>)new DbContextOptionsBuilder<TContext>()
            .UseModuleDatabase(ModuleDatabase.Postgres("Host=localhost;Database=minishop"), schema)
            .Options;
}

internal sealed class OrderingPostgresFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<OrderingDbContext>(OrderingDbContext.Schema));
}

internal sealed class InventoryPostgresFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<InventoryDbContext>(InventoryDbContext.Schema));
}

internal sealed class JourneysPostgresFactory : IDesignTimeDbContextFactory<JourneysDbContext>
{
    public JourneysDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<JourneysDbContext>(JourneysDbContext.Schema));
}

internal sealed class NotificationsPostgresFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<NotificationsDbContext>(NotificationsDbContext.Schema));
}

internal sealed class PaymentsPostgresFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<PaymentsDbContext>(PaymentsDbContext.Schema));
}

internal sealed class ReportingPostgresFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<ReportingDbContext>(ReportingDbContext.Schema));
}

internal sealed class ReviewsPostgresFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<ReviewsDbContext>(ReviewsDbContext.Schema));
}

internal sealed class AccountsPostgresFactory : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args) => new(PostgresDesignTime.Options<AccountsDbContext>(AccountsDbContext.Schema));
}
