using System.ComponentModel.DataAnnotations;

namespace MiniShop.Application.Ordering;

public sealed record OrderItemDto(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Discount,
    decimal LineTotal);

public sealed record OrderDto(
    int Id,
    string CustomerName,
    string CustomerEmail,
    DateTime CreatedAtUtc,
    string Status,
    decimal TotalPrice,
    IReadOnlyList<OrderItemDto> Items,
    string? RejectionReason);

/// <summary>
/// Produkt v nabídce – z kopie katalogu (krok 10), bez přesné zásoby (ta patří skladu).
/// E-shop krok 2: i popis, kategorie a dostupnost (<c>Unknown</c>, <c>InStock</c>, <c>LowStock</c>, <c>OutOfStock</c>),
/// krok 2B jméno souboru obrázku (Web ho ukazuje přes /media/products/{soubor}).
/// </summary>
public sealed record CatalogItemDto(
    int ProductId,
    string Name,
    string? Description,
    decimal Price,
    int? CategoryId,
    string? CategoryName,
    string Availability,
    string? ImageFileName = null);

/// <summary>
/// Nová objednávka. Účty krok 3: bez jména a e-mailu – zákazník je přihlášený uživatel (<see cref="OrderCustomer"/>).
/// </summary>
public sealed class CreateOrderRequest
{
    [MinLength(1, ErrorMessage = "Objednávka musí obsahovat alespoň jednu položku.")]
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, 1_000)]
    public int Quantity { get; set; }
}

/// <summary>
/// Účty krok 6: objednávky jednoho zákazníka v přehledu uživatelů pro admina. <paramref name="TotalSpent"/> = součet
/// potvrzených a vyřízených objednávek (zamítnuté, zrušené a čekající se nepočítají).
/// </summary>
public sealed record CustomerOrderSummaryDto(string CustomerId, int OrderCount, decimal TotalSpent);
