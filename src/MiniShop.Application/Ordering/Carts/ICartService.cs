namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 3: use-casy košíku. Zákazníka dostává parametrem (ID účtu) – stejně jako objednávky
/// (<see cref="OrderCustomer"/>), převod z přihlášeného uživatele dělá Web.
/// </summary>
public interface ICartService
{
    /// <summary>Košík s aktuálními cenami a dostupností z kopie katalogu (prázdný, když ještě neexistuje).</summary>
    Task<CartDto> GetAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>Počet kusů pro hlavičku webu.</summary>
    Task<int> GetTotalQuantityAsync(string customerId, CancellationToken cancellationToken = default);

    /// <returns>Název přidaného produktu (pro hlášku „Přidáno do košíku“).</returns>
    Task<string> AddItemAsync(string customerId, int productId, int quantity, CancellationToken cancellationToken = default);

    Task ChangeQuantityAsync(string customerId, int productId, int quantity, CancellationToken cancellationToken = default);

    Task RemoveItemAsync(string customerId, int productId, CancellationToken cancellationToken = default);
}
