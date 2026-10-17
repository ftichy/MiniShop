namespace MiniShop.Infrastructure.Inbox;

/// <summary>
/// Řádek v tabulce ProcessedMessages (inbox, krok 6): "handler X už zpracoval událost Y".
/// Zapisuje se ve STEJNÉ transakci jako změny, které handler udělal – buď obojí, nebo nic.
/// </summary>
public sealed class ProcessedMessage
{
    public Guid EventId { get; init; }

    /// <summary>Plný název třídy handleru. Každý handler si odškrtává sám za sebe.</summary>
    public string Handler { get; init; } = string.Empty;

    public DateTime ProcessedAtUtc { get; init; }
}
