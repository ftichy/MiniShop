using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Carts;

/// <summary>
/// E-shop krok 3: řádek košíku – produkt a množství. Mění ho jen <see cref="Cart"/> (metody jsou internal).
/// </summary>
public sealed class CartLine
{
    // Konstruktor pro EF Core.
    private CartLine()
    {
    }

    internal CartLine(int productId, int quantity, decimal priceWhenAdded)
    {
        ProductId = productId;
        SetQuantity(quantity);
        PriceWhenAdded = priceWhenAdded;
    }

    public int Id { get; private set; }
    public int CartId { get; private set; }

    /// <summary>ID produktu ve skladu (stejné jako v kopii katalogu).</summary>
    public int ProductId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Cena při posledním přidání – jen pro upozornění „cena se změnila“, závazná není.</summary>
    public decimal PriceWhenAdded { get; private set; }

    internal void Increase(int quantity, decimal currentPrice)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství musí být větší než nula.");
        }

        SetQuantity(Quantity + quantity);
        PriceWhenAdded = currentPrice;
    }

    internal void AcceptPrice(decimal currentPrice) => PriceWhenAdded = currentPrice;

    internal void SetQuantity(int quantity)
    {
        if (quantity is < 1 or > Cart.MaxQuantity)
        {
            throw new DomainException($"Množství musí být od 1 do {Cart.MaxQuantity} kusů.");
        }

        Quantity = quantity;
    }
}
