using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 10: opuštěný košík – zpožděná reakce na „ticho“. Worker se pravidelně zeptá, které košíky se dlouho
/// nezměnily, a pro každý oznámí fakt <see cref="CartAbandonedV1"/>. Komu a jestli vůbec psát, rozhodne Notifications.
/// </summary>
/// <remarks>
/// Značka „připomínka odešla“ a zpráva v outboxu se uloží v jedné transakci – připomínka nemůže odejít dvakrát
/// ani se ztratit. Když zákazník košík mezitím změní, uložení narazí na concurrency token (<c>CheckoutId</c>)
/// a košík se přeskočí – už není opuštěný.
/// </remarks>
public sealed class CartReminderService(
    ICartRepository carts,
    ICatalogProductRepository catalog,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    CartReminderSettings settings,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<int>> GetAbandonedCartIdsAsync(CancellationToken cancellationToken = default) =>
        carts.GetAbandonedIdsAsync(UtcNow - settings.AbandonedAfter, cancellationToken);

    /// <summary>Oznámí opuštění jednoho košíku. Vrací false, když už opuštěný není (změnil se, připomínka odešla).</summary>
    public async Task<bool> RemindAsync(int cartId, CancellationToken cancellationToken = default)
    {
        var cart = await carts.GetByIdAsync(cartId, cancellationToken);
        if (cart is null || !cart.IsAbandoned(UtcNow, settings.AbandonedAfter))
        {
            return false;
        }

        // Názvy a ceny z kopie katalogu (jako na stránce košíku); stažené produkty do připomínky nepatří.
        var products = await catalog.GetForCartAsync(cart, cancellationToken);
        var items = cart.Lines
            .OrderBy(l => l.Id)
            .Where(l => products.TryGetValue(l.ProductId, out var p) && !p.IsRemoved)
            .Select(l => new CartItemV1(l.ProductId, products[l.ProductId].Name, l.Quantity, products[l.ProductId].Price))
            .ToList();

        cart.MarkReminderSent(UtcNow, settings.AbandonedAfter);
        if (items.Count > 0)
        {
            publisher.Publish(new CartAbandonedV1(Guid.NewGuid(), UtcNow, cart.Id, cart.CustomerId, items));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return items.Count > 0;
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <summary>E-shop krok 10: nastavení připomínek (sekce Carts v appsettings.json).</summary>
/// <param name="AbandonedAfter">Jak dlouho se košík nesmí změnit, aby byl „opuštěný“ (výchozí 24 h).</param>
/// <param name="CheckInterval">Jak často worker košíky prochází (výchozí 15 min).</param>
public sealed record CartReminderSettings(TimeSpan AbandonedAfter, TimeSpan CheckInterval);
