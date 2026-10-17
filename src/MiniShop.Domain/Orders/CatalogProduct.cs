namespace MiniShop.Domain.Orders;

/// <summary>
/// Lokální kopie produktu v modulu Ordering (read model, krok 10): jen to, co objednávka potřebuje – název a cena.
/// Plní ji události ProductUpdatedV1 / ProductRemovedV1 ze skladu. Ordering tak nikdy nečte databázi skladu.
/// </summary>
/// <remarks>
/// Kopie je "eventually consistent": nový produkt se v ní objeví až za pár sekund.
/// Zprávy můžou přijít v jiném pořadí (retry) – proto si pamatuje čas poslední změny a starší změnu ignoruje.
/// Odebraný produkt se nemaže, jen označí (IsRemoved): pozdě doručená starší změna by ho jinak vzkřísila.
/// </remarks>
public sealed class CatalogProduct
{
    // Konstruktor pro EF Core.
    private CatalogProduct()
    {
    }

    public CatalogProduct(int id, string name, decimal price, DateTime changedAtUtc)
    {
        Id = id;
        Name = name;
        Price = price;
        ChangedAtUtc = changedAtUtc;
    }

    /// <summary>ID produktu ve skladu (není generované tady).</summary>
    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public bool IsRemoved { get; private set; }

    /// <summary>Kdy se stala poslední promítnutá změna (OccurredAtUtc ze zprávy).</summary>
    public DateTime ChangedAtUtc { get; private set; }

    /// <returns>false, když je změna starší než to, co už kopie zná (zpráva přišla pozdě).</returns>
    public bool Apply(string name, decimal price, DateTime changedAtUtc)
    {
        if (changedAtUtc < ChangedAtUtc)
        {
            return false;
        }

        Name = name;
        Price = price;
        ChangedAtUtc = changedAtUtc;
        return true;
    }

    public bool MarkRemoved(DateTime removedAtUtc)
    {
        if (removedAtUtc < ChangedAtUtc)
        {
            return false;
        }

        IsRemoved = true;
        ChangedAtUtc = removedAtUtc;
        return true;
    }
}
