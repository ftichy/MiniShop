using System.ComponentModel.DataAnnotations;

namespace MiniShop.Application.Ordering;

public sealed record OrderItemDto(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Discount,
    decimal LineTotal);

/// <param name="TotalPrice">Celkem k zaplacení – položky po slevách + doprava.</param>
/// <param name="ShippingMethod">E-shop krok 4: <c>None</c> (bez pokladny), <c>Pickup</c>, <c>Courier</c>.</param>
/// <param name="DeliveryAddress">E-shop krok 4: adresa jedním řádkem (jen kurýr).</param>
/// <param name="Carrier">E-shop krok 6: dopravce (jen odeslaná objednávka kurýrem).</param>
/// <param name="TrackingNumber">E-shop krok 6: číslo zásilky u dopravce.</param>
/// <param name="PaymentMethod">E-shop krok 8: <c>None</c> (bez pokladny), <c>Card</c>, <c>CashOnDelivery</c>.</param>
public sealed record OrderDto(
    int Id,
    string CustomerName,
    string CustomerEmail,
    DateTime CreatedAtUtc,
    string Status,
    decimal TotalPrice,
    IReadOnlyList<OrderItemDto> Items,
    string? RejectionReason,
    string ShippingMethod = "None",
    decimal ShippingPrice = 0m,
    string? DeliveryAddress = null,
    string? Carrier = null,
    string? TrackingNumber = null,
    string PaymentMethod = "None",
    string? CouponCode = null,
    decimal CouponDiscount = 0m);

/// <summary>E-shop krok 5: jeden řádek historie stavů. <paramref name="ChangedAtUtc"/> = null u objednávek starších než krok 5.</summary>
public sealed record OrderStatusChangeDto(string Status, DateTime? ChangedAtUtc, string? Note);

/// <summary>E-shop krok 5: detail objednávky (stránka /Orders/{id}) – objednávka a časová osa jejích stavů.</summary>
/// <param name="ReturnableUntilUtc">E-shop krok 12: do kdy jde objednávku vrátit; null = vrátit nejde (nedoručená, po termínu, vratka běží, nic nezbývá).</param>
/// <param name="Returns">E-shop krok 22: vratky po částech (null = žádné).</param>
/// <param name="ReturnableItems">E-shop krok 22: kolik kusů čeho jde ještě vrátit (formulář vratky); null = vrátit nejde.</param>
/// <param name="CustomerId">Účty krok 12A: vlastník objednávky (ID účtu) – zaměstnanec, který je i zákazník, vidí akce zákazníka jen u svých.</param>
public sealed record OrderDetailDto(
    OrderDto Order,
    IReadOnlyList<OrderStatusChangeDto> History,
    DateTime? ReturnableUntilUtc = null,
    string? ReturnReason = null,
    string? ReturnRejectionReason = null,
    IReadOnlyList<OrderReturnDto>? Returns = null,
    IReadOnlyList<ReturnableItemDto>? ReturnableItems = null,
    string? CustomerId = null);

/// <summary>E-shop krok 22: vratka po částech na detailu objednávky a na stránce Vratky.</summary>
/// <param name="Status"><c>Requested</c>, <c>Completed</c>, <c>Rejected</c>.</param>
public sealed record OrderReturnDto(
    int Number,
    string Status,
    string Reason,
    DateTime RequestedAtUtc,
    DateTime? ResolvedAtUtc,
    string? RejectionReason,
    decimal Amount,
    IReadOnlyList<OrderReturnLineDto> Lines);

public sealed record OrderReturnLineDto(int ProductId, string ProductName, int Quantity, decimal LineTotal);

/// <summary>E-shop krok 22: položka, ze které jde ještě vracet – nejvýš <paramref name="MaxQuantity"/> kusů.</summary>
public sealed record ReturnableItemDto(int ProductId, string ProductName, int MaxQuantity);

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
    string? ImageFileName = null,
    decimal RatingAverage = 0m,
    int RatingCount = 0);

/// <summary>
/// Nová objednávka. Účty krok 3: bez jména a e-mailu – zákazník je přihlášený uživatel (<see cref="OrderCustomer"/>).
/// </summary>
public sealed class CreateOrderRequest
{
    [MinLength(1, ErrorMessage = "Objednávka musí obsahovat alespoň jednu položku.")]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    /// <summary>
    /// E-shop krok 4 (volitelné): ID odeslání, které si klient vygeneruje. Opakovaný požadavek se stejným ID
    /// (třeba po výpadku sítě) nezaloží druhou objednávku, ale vrátí tu první.
    /// </summary>
    public Guid? CheckoutId { get; set; }
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
