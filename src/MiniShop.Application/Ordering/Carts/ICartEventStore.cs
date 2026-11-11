using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 17: čtení událostí košíků (event store, tabulka <c>CartEvents</c>). Zapisuje je <c>OrderingDbContext</c>
/// při uložení košíku; načtení košíku (přehrání od poslední „nuly“) dělá <see cref="ICartRepository"/>.
/// Tady jen čtení pro nové pohledy na historii – projekce při čtení (historie zákazníka, statistiky pro admina).
/// </summary>
public interface ICartEventStore
{
    /// <summary>Posledních <paramref name="count"/> událostí košíku zákazníka (nejnovější poslední), nebo prázdné.</summary>
    Task<IReadOnlyList<VersionedCartEvent>> GetLatestForCustomerAsync(string customerId, int count,
        CancellationToken cancellationToken = default);

    /// <summary>Všechny události všech košíků po proudech (statistiky). Klíč = ID košíku, události podle verze.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<VersionedCartEvent>>> GetAllStreamsAsync(CancellationToken cancellationToken = default);
}
