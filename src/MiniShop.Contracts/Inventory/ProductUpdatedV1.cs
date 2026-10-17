namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Produkt byl založen nebo se změnil jeho název / cena (krok 10). Nese CELÝ aktuální stav ("fat event"),
/// takže příjemce si podle něj udrží vlastní kopii katalogu a nemusí se sklad na nic ptát.
/// </summary>
public sealed record ProductUpdatedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId,
    string Name,
    decimal Price) : IIntegrationEvent;
