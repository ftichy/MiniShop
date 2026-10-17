using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Inventory;

/// <summary>Odesílání integračních zpráv z modulu Inventory – zapisuje do outboxu v inventory.db (krok 10).</summary>
public interface IInventoryMessagePublisher : IIntegrationMessagePublisher;
