namespace MiniShop.Contracts.Inventory;

/// <summary>
/// E-shop krok 2: produkt byl založen nebo se změnil – CELÝ aktuální stav pro výlohu ("fat event"):
/// k názvu a ceně z <see cref="ProductUpdatedV1"/> navíc popis, kategorie, obrázek a dostupnost.
/// </summary>
/// <remarks>
/// V1 se nezměnila (kontrakty se jen verzují) a sklad ji zatím posílá dál vedle V2 (expand → contract).
/// <paramref name="ImageFileName"/> je jen jméno souboru ve skladu (null = bez obrázku), ne obrázek samotný.
/// <paramref name="Availability"/> je hodnota z <see cref="StockAvailabilityV1"/>; platí k času
/// <paramref name="AvailabilityChangedAtUtc"/> (pozdější <see cref="StockAvailabilityChangedV1"/> ji přepíše).
/// </remarks>
public sealed record ProductUpdatedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int ProductId,
    string Name,
    string? Description,
    decimal Price,
    int CategoryId,
    string CategoryName,
    string? ImageFileName,
    string Availability,
    DateTime AvailabilityChangedAtUtc) : IIntegrationEvent;
