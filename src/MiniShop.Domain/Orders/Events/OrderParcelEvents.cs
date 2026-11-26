using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 28C: skladník předal dopravci jeden balík objednávky – z ní vznikne příkaz <c>CreateShipmentV2</c>.</summary>
public sealed record OrderParcelShipmentRequested(Order Order, int ParcelNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 28C: dopravce převzal jeden balík – z ní vznikne <c>ParcelShippedV1</c> (e-mail „balík je na cestě“).</summary>
public sealed record OrderParcelShipped(Order Order, int ParcelNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>
/// E-shop krok 28D: dopravce vrátil jeden balík rozdělené objednávky – vznikla automatická vratka po částech
/// (<see cref="ReturnNumber"/>). Sága vratky začne rovnou naskladněním (zboží už je zpátky), zákazník nic neposílá.
/// </summary>
public sealed record OrderParcelReturnedToSender(Order Order, int ReturnNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
