namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 6: objednávka odešla zákazníkovi (osobní odběr: je připravená k vyzvednutí). Sklad podle ní změní
/// rezervaci na výdej, notifikace pošlou číslo zásilky. Dopravce a číslo zásilky jsou jen u kurýra.
/// </summary>
public sealed record OrderShippedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    string? Carrier,
    string? TrackingNumber) : IIntegrationEvent;
