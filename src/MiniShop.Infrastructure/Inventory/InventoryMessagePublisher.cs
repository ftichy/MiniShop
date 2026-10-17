using MiniShop.Application.Inventory;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Infrastructure.Inventory;

/// <summary>Zprávy z modulu Inventory jdou do outboxu v inventory.db (krok 10).</summary>
public sealed class InventoryMessagePublisher(InventoryDbContext db)
    : OutboxIntegrationMessagePublisher(db), IInventoryMessagePublisher;
