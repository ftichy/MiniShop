using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Notifications;

/// <summary>
/// Lísteček „dejte mi vědět, až zboží přijde“: zákazník chce jeden e-mail o naskladnění produktu. Po odeslání e-mailu
/// se řádek smaže – e-mail tak přijde jen jednou; kdo chce další, požádá znovu.
/// </summary>
/// <remarks>Obsahuje vazbu zákazník ↔ produkt (osobní údaj) – po smazání účtu se smaže (CustomerAnonymizedV1).</remarks>
public class BackInStockSubscription
{
    // Konstruktor pro EF Core.
    private BackInStockSubscription()
    {
    }

    public BackInStockSubscription(string customerId, int productId, string productName, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Upozornění na naskladnění potřebuje zákazníka.");
        }

        if (productId <= 0)
        {
            throw new DomainException("Upozornění na naskladnění potřebuje produkt.");
        }

        CustomerId = customerId;
        ProductId = productId;
        ProductName = productName;
        CreatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    public int ProductId { get; private set; }

    /// <summary>Název z doby přihlášení (zpráva o dostupnosti ho nenese).</summary>
    public string ProductName { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
}
