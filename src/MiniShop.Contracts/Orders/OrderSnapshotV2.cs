namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 14: aktuální stav objednávky i se zákazníkem – odpověď na <see cref="Reviews.SyncPurchasesV1"/>.
/// <see cref="OrderSnapshotV1"/> zákazníka záměrně nenese (přehled tržeb osobní údaje nepotřebuje), modul Reviews ano:
/// potřebuje vědět, KDO co koupil, aby smí hodnotit jen ten, kdo zboží dostal.
/// </summary>
/// <remarks>
/// Nese osobní údaje (ID účtu, jméno) – odběratel musí poslouchat i <see cref="Customers.CustomerAnonymizedV1"/>.
/// Anonymizované objednávky Ordering neposílá.
/// </remarks>
/// <param name="Status">Název stavu objednávky (<c>Completed</c> = doručená, <c>Returned</c> = vrácená…).</param>
public sealed record OrderSnapshotV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerId,
    string CustomerName,
    DateTime PlacedAtUtc,
    string Status,
    IReadOnlyList<OrderItemV2> Items) : IIntegrationEvent;
