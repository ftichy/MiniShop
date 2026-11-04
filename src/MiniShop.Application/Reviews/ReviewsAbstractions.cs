using MiniShop.Application.Abstractions;
using MiniShop.Domain.Reviews;

namespace MiniShop.Application.Reviews;

/// <summary>E-shop krok 14: uložení změn modulu Reviews – hodnocení, kopie nákupů, inbox a outbox v reviews.db.</summary>
public interface IReviewsUnitOfWork : IUnitOfWork;

/// <summary>E-shop krok 14: zprávy z Reviews (souhrn hodnocení, žádost o nákupy) jdou do outboxu v reviews.db.</summary>
public interface IReviewsMessagePublisher : IIntegrationMessagePublisher;

/// <summary>E-shop krok 14: hodnocení produktů.</summary>
public interface IReviewRepository
{
    /// <summary>Všechna hodnocení produktu (i skrytá) – podklad pro souhrn i pro stránku produktu.</summary>
    Task<IReadOnlyList<Review>> GetForProductAsync(int productId, CancellationToken cancellationToken = default);

    Task<Review?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Nejnovější hodnocení napříč produkty (stránka Hodnocení pro admina).</summary>
    Task<IReadOnlyList<Review>> GetLatestAsync(int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Review>> GetByCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    void Add(Review review);
}

/// <summary>E-shop krok 14: kopie objednávek – kdo co koupil a dostal.</summary>
public interface IPurchaseRepository
{
    Task<PurchasedOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Doručená a nevrácená objednávka zákazníka s tímhle produktem, nebo null (hodnotit nesmí).</summary>
    Task<PurchasedOrder?> FindReviewableAsync(string customerId, int productId, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Zapomenout zákazníka (smazal účet) – smaže všechny jeho objednávky z kopie.</summary>
    Task RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    void Add(PurchasedOrder order);
}
