namespace MiniShop.Contracts.Orders;

/// <summary>Položka objednávky v integračních událostech.</summary>
public sealed record OrderLineV1(int ProductId, int Quantity);
