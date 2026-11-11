using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Application.Ordering.Carts;

/// <summary>
/// E-shop krok 17: nové pohledy na košíky z jejich událostí – <b>projekce při čtení</b> (jako průběh objednávky
/// v Journeys). Hlavní výhoda event sourcingu: na otázku, kterou jsme si dřív nepoložili („co zákazníci z košíku
/// odebírají?“), odpovíme i zpětně – data se nezahazovala. Nic se kvůli tomu neukládá navíc.
/// </summary>
public sealed class CartHistoryService(ICartEventStore events, ICatalogProductRepository catalog)
{
    public const int HistoryLength = 30;
    public const int TopRemovedCount = 10;

    /// <summary>Posledních <see cref="HistoryLength"/> změn košíku zákazníka (nejnovější první).</summary>
    public async Task<IReadOnlyList<CartHistoryEntryDto>> GetHistoryAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var history = await events.GetLatestForCustomerAsync(customerId, HistoryLength, cancellationToken);
        var names = await NamesAsync(history.SelectMany(e => ProductIds(e.Event)), cancellationToken);
        return history
            .Select(e => new CartHistoryEntryDto(e.Event.OccurredAtUtc, Describe(e.Event, names)))
            .Reverse()
            .ToList();
    }

    /// <summary>Statistika košíků pro admina – spočítaná z událostí všech košíků.</summary>
    public async Task<CartInsightsDto> GetInsightsAsync(CancellationToken cancellationToken = default)
    {
        var streams = await events.GetAllStreamsAsync(cancellationToken);
        var all = streams.Values.SelectMany(stream => stream).Select(e => e.Event).ToList();

        // Otevřený nákup = košík, který po přehrání událostí (od poslední „nuly“) něco obsahuje.
        var open = streams.Values.Count(stream => Cart.FromHistory("statistika", Cart.FromLastReset(stream)).Lines.Count > 0);

        var removed = all.OfType<CartItemRemoved>()
            .GroupBy(e => e.ProductId)
            .Select(g => (ProductId: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ProductId)
            .Take(TopRemovedCount)
            .ToList();
        var names = await NamesAsync(removed.Select(x => x.ProductId), cancellationToken);

        return new CartInsightsDto(
            all.Count == 0 ? null : all.Min(e => e.OccurredAtUtc),
            all.Count,
            all.OfType<CartCheckedOut>().Count(),
            all.OfType<CartCleared>().Count(),
            open,
            all.OfType<CartReminderSent>().Count(),
            removed.Select(x => new RemovedProductDto(x.ProductId, names.GetValueOrDefault(x.ProductId) ?? Unknown(x.ProductId), x.Count)).ToList());
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : (await catalog.GetByIdsAsync(ids, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
    }

    private static IEnumerable<int> ProductIds(CartEvent cartEvent) => cartEvent switch
    {
        CartItemAdded e => [e.ProductId],
        CartItemQuantityChanged e => [e.ProductId],
        CartItemRemoved e => [e.ProductId],
        _ => []
    };

    /// <summary>Událost česky pro zákazníka.</summary>
    private static string Describe(CartEvent cartEvent, IReadOnlyDictionary<int, string> names)
    {
        string Name(int id) => names.GetValueOrDefault(id) ?? Unknown(id);

        return cartEvent switch
        {
            CartItemAdded e => $"Přidáno: {Name(e.ProductId)} ({e.Quantity} ks)",
            CartItemQuantityChanged e => $"Změna množství: {Name(e.ProductId)} na {e.Quantity} ks",
            CartItemRemoved e => $"Odebráno: {Name(e.ProductId)}",
            CartPricesAccepted => "Potvrzeny nové ceny",
            CartCouponApplied e => $"Uplatněn slevový kód {e.CouponCode}",
            CartCouponRemoved => "Odebrán slevový kód",
            CartCheckedOut e => $"Objednáno ({e.Lines.Sum(l => l.Quantity)} ks)",
            CartCleared => "Košík vyprázdněn",
            CartReminderSent => "Odeslána připomínka zapomenutého košíku",
            CartSnapshotTaken { Reason: CartSnapshotTaken.ImportedReason } e => $"Převzat obsah košíku z doby před historií ({e.Lines.Sum(l => l.Quantity)} ks)",
            CartSnapshotTaken e => $"Obsah košíku srovnán ({e.Lines.Sum(l => l.Quantity)} ks)",
            _ => cartEvent.GetType().Name
        };
    }

    private static string Unknown(int productId) => $"produkt č. {productId}";
}

/// <summary>Jeden řádek historie košíku.</summary>
public sealed record CartHistoryEntryDto(DateTime OccurredAtUtc, string Text);

/// <summary>Statistika košíků (e-shop krok 17).</summary>
/// <param name="Since">Nejstarší událost – dřív se historie nevedla (null = zatím žádná).</param>
/// <param name="CheckedOut">Nákupy, které skončily objednávkou.</param>
/// <param name="Cleared">Košíky vyprázdněné bez objednávky.</param>
/// <param name="OpenCarts">Košíky, ve kterých teď něco je.</param>
/// <param name="RemindersSent">Odeslané připomínky zapomenutého košíku.</param>
/// <param name="TopRemoved">Produkty nejčastěji odebrané z košíku (zákazník si to rozmyslel).</param>
public sealed record CartInsightsDto(
    DateTime? Since,
    int EventCount,
    int CheckedOut,
    int Cleared,
    int OpenCarts,
    int RemindersSent,
    IReadOnlyList<RemovedProductDto> TopRemoved);

public sealed record RemovedProductDto(int ProductId, string Name, int Count);
