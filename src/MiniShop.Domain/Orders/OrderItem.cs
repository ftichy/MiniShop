using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders;

/// <summary>
/// Položka objednávky. Název a cena produktu se ukládají jako "snímek" v okamžiku objednání,
/// takže pozdější změna ceny produktu neovlivní už vytvořené objednávky.
/// </summary>
public class OrderItem
{
    // Konstruktor pro EF Core.
    private OrderItem()
    {
    }

    internal OrderItem(int productId, string productName, decimal unitPrice, int quantity, decimal discount)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství položky musí být větší než nula.");
        }

        if (discount < 0 || discount > unitPrice * quantity)
        {
            throw new DomainException("Sleva musí být mezi nulou a cenou položky.");
        }

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        Discount = discount;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal Discount { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity - Discount;
}
