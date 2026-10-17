namespace MiniShop.Contracts.Inventory;

/// <summary>Položka rezervace skladu.</summary>
public sealed record StockLineV1(int ProductId, int Quantity);
