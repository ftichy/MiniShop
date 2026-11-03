using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>E-shop krok 7: vytvoří nebo aktualizuje notifications.db (EF migrace, jako ostatní moduly).</summary>
public static class NotificationsDatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(), cancellationToken);
    }

    /// <summary>Databáze vznikla až s migracemi – značková tabulka jen pro jednotné volání <see cref="DatabaseMigrator"/>.</summary>
    public static Task MigrateAsync(NotificationsDbContext dbContext, CancellationToken cancellationToken = default) =>
        DatabaseMigrator.MigrateAsync(dbContext, "OrderRecipients", cancellationToken);
}
