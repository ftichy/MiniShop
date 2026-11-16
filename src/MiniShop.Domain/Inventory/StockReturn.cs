using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Inventory;

/// <summary>
/// E-shop krok 22: záznam skladu, že vratka objednávky (číslo <see cref="ReturnNumber"/>) je naskladněná.
/// Pojistka nad rámec inboxu: stejná vratka se nenaskladní dvakrát, ani kdyby příkaz přišel s jiným ID (unikátní
/// OrderId + ReturnNumber). Sklad o objednávce dál nic neví – jen čísla z příkazu.
/// </summary>
public sealed class StockReturn
{
    // Konstruktor pro EF Core.
    private StockReturn()
    {
    }

    public StockReturn(int orderId, int returnNumber, int quantity, DateTime restockedAtUtc)
    {
        if (orderId <= 0 || returnNumber <= 0)
        {
            throw new DomainException("Vratka potřebuje číslo objednávky a pořadí vratky.");
        }

        OrderId = orderId;
        ReturnNumber = returnNumber;
        Quantity = quantity;
        RestockedAtUtc = restockedAtUtc;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int ReturnNumber { get; private set; }

    /// <summary>Kolik kusů se opravdu naskladnilo.</summary>
    public int Quantity { get; private set; }

    public DateTime RestockedAtUtc { get; private set; }
}
