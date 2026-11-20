using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// E-shop krok 26B: skladník předal zabalenou objednávku dopravci – z ní vznikne příkaz <c>CreateShipmentV1</c>.
/// Od této chvíle objednávku zrušit nejde (zásilka už může u dopravce existovat).
/// </summary>
public sealed record OrderShipmentRequested(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
