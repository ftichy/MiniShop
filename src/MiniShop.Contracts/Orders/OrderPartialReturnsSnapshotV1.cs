namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 22: vyřízené vratky po částech jedné objednávky jako odpověď na <see cref="Reporting.RebuildSalesReportV1"/> –
/// posílá se vedle <see cref="OrderSnapshotV1"/> (ten zůstává beze změny), jen u objednávek, které nějakou mají.
/// Nejde o nový fakt – proto ne <see cref="ReturnCompletedV2"/> (to by vystavilo dobropisy a rozeslalo e-maily znovu).
/// </summary>
public sealed record OrderPartialReturnsSnapshotV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    IReadOnlyList<PartialReturnSnapshotV1> Returns) : IIntegrationEvent;

/// <summary>E-shop krok 22: jedna vyřízená vratka po částech ve snímku.</summary>
/// <param name="Amount">Kolik se vrátilo celkem (položky + doprava).</param>
public sealed record PartialReturnSnapshotV1(int ReturnNumber, decimal Amount, IReadOnlyList<ReturnItemV1> Items);
