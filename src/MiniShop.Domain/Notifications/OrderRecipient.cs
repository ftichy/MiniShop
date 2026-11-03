using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Notifications;

/// <summary>
/// E-shop krok 7: komu a o čem psát u jedné objednávky – kopie údajů z události OrderPlacedV2 (jméno, e-mail, položky,
/// doprava). Další události objednávky pak nesou hlavně číslo objednávky a Notifications si zbytek najde tady.
/// </summary>
/// <remarks>
/// Stejný vzor jako kopie katalogu v Orderingu: modul si drží vlastní data a nesahá do cizí databáze.
/// Obsahuje osobní údaje – po smazání účtu (CustomerAnonymizedV1) se řádky zákazníka smažou.
/// </remarks>
public class OrderRecipient
{
    private readonly List<OrderRecipientItem> _items = new();

    // Konstruktor pro EF Core.
    private OrderRecipient()
    {
    }

    public OrderRecipient(int orderId, string customerId, string customerName, string customerEmail,
        IEnumerable<OrderRecipientItem> items, string shippingMethod, decimal shippingPrice, string? deliveryAddress, decimal totalPrice)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Příjemce oznámení potřebuje číslo objednávky.");
        }

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Příjemce oznámení potřebuje zákazníka.");
        }

        OrderId = orderId;
        CustomerId = customerId;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        _items.AddRange(items);
        ShippingMethod = shippingMethod;
        ShippingPrice = shippingPrice;
        DeliveryAddress = deliveryAddress;
        TotalPrice = totalPrice;
    }

    /// <summary>Číslo objednávky – zároveň klíč (jedna objednávka = jeden příjemce).</summary>
    public int OrderId { get; private set; }

    /// <summary>ID účtu zákazníka – podle něj se po smazání účtu najdou jeho řádky.</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    /// <summary>Prázdný = není komu psát (objednávka už byla v Orderingu anonymizovaná).</summary>
    public string CustomerEmail { get; private set; } = string.Empty;

    public IReadOnlyCollection<OrderRecipientItem> Items => _items.AsReadOnly();

    /// <summary><c>None</c>, <c>Pickup</c>, <c>Courier</c> – jako v kontraktu (Notifications Ordering nezná).</summary>
    public string ShippingMethod { get; private set; } = "None";

    public decimal ShippingPrice { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public decimal TotalPrice { get; private set; }

    public bool IsPickup => ShippingMethod == "Pickup";
}

/// <summary>Položka do souhrnu v e-mailu (snímek z doby objednání).</summary>
public class OrderRecipientItem
{
    // Konstruktor pro EF Core.
    private OrderRecipientItem()
    {
    }

    public OrderRecipientItem(string productName, int quantity, decimal lineTotal)
    {
        ProductName = productName;
        Quantity = quantity;
        LineTotal = lineTotal;
    }

    public int Id { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }
}
