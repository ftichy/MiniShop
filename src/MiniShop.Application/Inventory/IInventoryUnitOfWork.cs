using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Inventory;

/// <summary>Uložení změn modulu Inventory (krok 10): produkty, rezervace a jejich outbox v databázi inventory.db.</summary>
public interface IInventoryUnitOfWork : IUnitOfWork;
