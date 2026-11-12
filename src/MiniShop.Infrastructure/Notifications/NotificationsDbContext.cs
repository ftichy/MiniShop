using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Common;
using MiniShop.Domain.Notifications;
using MiniShop.Infrastructure.Notifications.Configurations;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Notifications;

/// <summary>
/// E-shop krok 7: databáze modulu Notifications – notifications.db: příjemci oznámení (kopie z OrderPlacedV2) a inbox.
/// Dědí z <see cref="ModuleDbContext"/> kvůli inboxu (každá zpráva = nejvýš jeden e-mail); outbox má, ale nepoužívá –
/// modul nic neposílá, jen poslouchá.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options), INotificationsUnitOfWork
{
    /// <summary>Škálování krok 1: schéma modulu v PostgreSQL (vlastní tabulky i historie migrací).</summary>
    public const string Schema = "notifications";

    public DbSet<OrderRecipient> OrderRecipients => Set<OrderRecipient>();

    public DbSet<BackInStockSubscription> BackInStockSubscriptions => Set<BackInStockSubscription>();

    // Notifications nemá doménové události – kdyby nějaká vznikla, chyba se ukáže hned.
    protected override IEnumerable<OutboxMessage> ToOutboxMessages(IDomainEvent domainEvent) =>
        throw new InvalidOperationException($"Modul Notifications neposílá zprávy ({domainEvent.GetType().Name}).");

    protected override void ConfigureModule(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly,
            type => type.Namespace == typeof(OrderRecipientConfiguration).Namespace);
}
