namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 22: jedna položka vratky po částech – co a za kolik se vrací (ceny z doby objednání).
/// </summary>
/// <param name="Quantity">Kolik kusů se vrací (může být méně, než se koupilo).</param>
/// <param name="UnitPrice">Cena za kus z objednávky.</param>
/// <param name="Discount">Podíl slevy z kuponu připadající na vrácené kusy.</param>
/// <param name="LineTotal">Kolik se za položku vrací (<c>UnitPrice * Quantity - Discount</c>).</param>
public sealed record ReturnItemV1(int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Discount, decimal LineTotal);
