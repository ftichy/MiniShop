namespace MiniShop.Application.Inventory;

/// <summary>E-shop krok 2: správa kategorií produktů (modul Inventory). Ve Webu ji implementuje InventoryApiClient.</summary>
public interface ICategoryService
{
    /// <summary>Všechny kategorie, seřazené podle názvu (česky).</summary>
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Přejmenuje kategorii – produkty v ní pošlou znovu ProductUpdatedV2 (jméno kategorie je ve zprávě).</summary>
    Task<CategoryDto> RenameAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Smaže prázdnou kategorii; s produkty DomainException.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
