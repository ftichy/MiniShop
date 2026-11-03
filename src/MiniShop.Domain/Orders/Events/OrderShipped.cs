using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// E-shop krok 6: objednávka odešla (kurýr – předaná dopravci s číslem zásilky; osobní odběr – připravená k vyzvednutí).
/// Od této chvíle ji zrušit nejde a rezervace na skladě se mění na výdej.
/// </summary>
public sealed record OrderShipped(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
