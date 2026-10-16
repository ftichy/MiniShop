using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Products;

/// <summary>
/// Produkt v nabídce obchodu. Stav se mění jen přes metody, které hlídají business pravidla
/// (zapouzdření – settery jsou privátní).
/// </summary>
public class Product : Entity
{
    // Konstruktor pro EF Core.
    private Product()
    {
    }

    public Product(string name, string? description, decimal price, int stockQuantity)
    {
        if (stockQuantity < 0)
        {
            throw new DomainException("Skladová zásoba nemůže být záporná.");
        }

        Update(name, description, price);
        StockQuantity = stockQuantity;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }

    public void Update(string name, string? description, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Název produktu je povinný.");
        }

        if (price <= 0)
        {
            throw new DomainException("Cena produktu musí být větší než nula.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = price;
    }

    public void AddStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství pro naskladnění musí být větší než nula.");
        }

        StockQuantity += quantity;
    }

    public void RemoveStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Množství pro vyskladnění musí být větší než nula.");
        }

        if (quantity > StockQuantity)
        {
            throw new InsufficientStockException(Name, quantity, StockQuantity);
        }

        StockQuantity -= quantity;
    }
}
