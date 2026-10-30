using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>E-shop krok 2: kategorie produktů (modul Inventory).</summary>
public interface ICategoryRepository
{
    /// <summary>Všechny kategorie, seřazené podle názvu.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Počet produktů v každé kategorii (kategorie bez produktů ve slovníku nejsou).</summary>
    Task<IReadOnlyDictionary<int, int>> CountProductsAsync(CancellationToken cancellationToken = default);

    void Add(Category category);
    void Remove(Category category);
}
