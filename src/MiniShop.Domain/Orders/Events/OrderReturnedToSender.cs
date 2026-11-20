using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// E-shop krok 26D: zásilka se vrátila od dopravce nedoručená. Z ní vznikne událost <c>OrderReturnedToSenderV1</c> –
/// sklad, platby, faktury, e-maily a tržby si na ni zareagují každý sám (choreografie, žádná sága).
/// </summary>
public sealed record OrderReturnedToSender(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
