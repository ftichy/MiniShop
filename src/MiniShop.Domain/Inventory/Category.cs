using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Inventory;

/// <summary>
/// E-shop krok 2: kategorie produktů (Periferie, Monitory…). Patří skladu – ten spravuje katalog.
/// Ordering kategorie nezná jako entity: jméno kategorie mu přijde v každé zprávě o produktu (ProductUpdatedV2).
/// </summary>
public sealed class Category
{
    // Konstruktor pro EF Core.
    private Category()
    {
    }

    public Category(string name)
    {
        Rename(name);
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Název kategorie je povinný.");
        }

        Name = name.Trim();
    }
}
