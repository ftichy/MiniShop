using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory.Events;

namespace MiniShop.Domain.Inventory;

/// <summary>
/// Produkt v nabídce obchodu. Stav se mění jen přes metody, které hlídají business pravidla
/// (zapouzdření – settery jsou privátní).
/// </summary>
/// <remarks>
/// Od kroku 10 patří modulu Inventory (vlastní databáze inventory.db). Ordering ho nevidí – drží si vlastní kopii
/// názvu a ceny (CatalogProduct), kterou plní události <see cref="ProductChanged"/> / <see cref="ProductRemoved"/>.
/// Změna zásoby (rezervace) událost nevyvolá – ordering o skladu nic vědět nepotřebuje.
/// </remarks>
public class Product : Entity
{
    // Konstruktor pro EF Core.
    private Product()
    {
    }

    public Product(string name, string? description, decimal price, int stockQuantity, DateTime createdAtUtc)
    {
        if (stockQuantity < 0)
        {
            throw new DomainException("Skladová zásoba nemůže být záporná.");
        }

        Update(name, description, price, createdAtUtc);
        StockQuantity = stockQuantity;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }

    public void Update(string name, string? description, decimal price, DateTime updatedAtUtc)
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

        // Nový i změněný produkt: Ordering si podle toho aktualizuje svou kopii katalogu (krok 10).
        // Při opakovaném Update před uložením stačí poslední stav – starší událost zahodíme.
        RemoveDomainEvents<ProductChanged>();
        Raise(new ProductChanged(this, updatedAtUtc));
    }

    /// <summary>Produkt se maže z nabídky – před odebráním z repozitáře, aby se dala vyvolat událost.</summary>
    public void MarkRemoved(DateTime removedAtUtc)
    {
        RemoveDomainEvents<ProductChanged>();
        Raise(new ProductRemoved(Id, removedAtUtc));
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
