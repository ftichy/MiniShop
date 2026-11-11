using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// E-shop krok 17: řádek tabulky <c>CartEvents</c> – jedna událost košíku. Jen INSERT; unikátní (<see cref="CartId"/>,
/// <see cref="Version"/>) = optimistický souběh: dvě souběžné změny téhož košíku chtějí stejnou verzi, druhá narazí.
/// </summary>
public sealed class CartEventRecord
{
    public long Id { get; set; }

    public int CartId { get; set; }

    /// <summary>Navigace jen kvůli EF: nový košík dostane ID až při uložení a EF ho dosadí i do jeho událostí.</summary>
    public Cart? Cart { get; set; }

    /// <summary>Pořadí v proudu košíku – 1, 2, 3… bez děr.</summary>
    public int Version { get; set; }

    /// <summary>Jméno typu události (<c>CartItemAdded</c>…) – neměnit, jinak staré události nepůjde přečíst.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Událost jako JSON.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>E-shop krok 17: převod událostí košíku na řádky tabulky a zpět (JSON, jméno typu jako text).</summary>
public static class CartEventSerializer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Seznam známých událostí: jméno v databázi → typ. Nová událost = nový řádek (staré jméno nikdy neměnit).
    private static readonly Dictionary<string, Type> Types = new[]
    {
        typeof(CartItemAdded), typeof(CartItemQuantityChanged), typeof(CartItemRemoved), typeof(CartPricesAccepted),
        typeof(CartCouponApplied), typeof(CartCouponRemoved), typeof(CartCheckedOut), typeof(CartCleared),
        typeof(CartReminderSent), typeof(CartSnapshotTaken)
    }.ToDictionary(type => type.Name);

    /// <summary>Jména typů, po kterých stav nezávisí na dřívějších událostech (<see cref="Cart.IsReset"/>).</summary>
    public static readonly string[] ResetTypes = [nameof(CartCheckedOut), nameof(CartCleared), nameof(CartSnapshotTaken)];

    public static CartEventRecord ToRecord(Cart cart, int version, CartEvent cartEvent) => new()
    {
        Cart = cart,
        Version = version,
        Type = cartEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(cartEvent, cartEvent.GetType(), Json),
        OccurredAtUtc = cartEvent.OccurredAtUtc
    };

    public static VersionedCartEvent FromRecord(CartEventRecord record) =>
        Types.TryGetValue(record.Type, out var type)
            ? new VersionedCartEvent(record.Version, (CartEvent)JsonSerializer.Deserialize(record.Payload, type, Json)!)
            : throw new InvalidOperationException($"Neznámý typ události košíku „{record.Type}“ (košík {record.CartId}, verze {record.Version}).");
}

/// <summary>E-shop krok 17: čtení událostí košíků pro historii a statistiky.</summary>
public sealed class CartEventStore(OrderingDbContext db) : ICartEventStore
{
    public async Task<IReadOnlyList<VersionedCartEvent>> GetLatestForCustomerAsync(string customerId, int count,
        CancellationToken cancellationToken = default)
    {
        var records = await db.CartEvents.AsNoTracking()
            .Where(e => e.Cart!.CustomerId == customerId)
            .OrderByDescending(e => e.Version)
            .Take(count)
            .ToListAsync(cancellationToken);
        return records.OrderBy(e => e.Version).Select(CartEventSerializer.FromRecord).ToList();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<VersionedCartEvent>>> GetAllStreamsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await db.CartEvents.AsNoTracking().OrderBy(e => e.CartId).ThenBy(e => e.Version).ToListAsync(cancellationToken);
        return records.GroupBy(e => e.CartId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<VersionedCartEvent>)g.Select(CartEventSerializer.FromRecord).ToList());
    }

    /// <summary>Proud košíku od poslední „nuly“ (objednáno, vyprázdněno, snapshot) – víc k sestavení stavu není potřeba.</summary>
    internal static async Task<IReadOnlyList<VersionedCartEvent>> GetFromLastResetAsync(OrderingDbContext db, int cartId,
        CancellationToken cancellationToken)
    {
        var lastReset = await db.CartEvents
            .Where(e => e.CartId == cartId && CartEventSerializer.ResetTypes.Contains(e.Type))
            .MaxAsync(e => (int?)e.Version, cancellationToken) ?? 1;
        var records = await db.CartEvents.AsNoTracking()
            .Where(e => e.CartId == cartId && e.Version >= lastReset)
            .OrderBy(e => e.Version)
            .ToListAsync(cancellationToken);
        return records.Select(CartEventSerializer.FromRecord).ToList();
    }
}
