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

/// <summary>Produkt v nabídce objednávek – z kopie katalogu (krok 10), bez zásoby (ta patří skladu).</summary>
public sealed record CatalogItemDto(int ProductId, string Name, decimal Price);

public sealed class CreateOrderRequest
{
    [Required, StringLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(200)]
    public string CustomerEmail { get; set; } = string.Empty;

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
