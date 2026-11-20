namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 26B: příkaz modulu Shipping – předat zásilku dopravci. Posílá ho Ordering, když skladník klikne
/// „Předat dopravci“. Odpověď přijde jako <see cref="ShipmentCreatedV1"/> (číslo zásilky) nebo <see cref="ShipmentFailedV1"/>.
/// </summary>
/// <remarks>
/// Nese osobní údaje (jméno a adresu příjemce) – jdou dál dopravci. <see cref="CommandId"/> je zároveň Idempotency-Key
/// u dopravce: opakované doručení téhož příkazu u něj druhou zásilku nezaloží.
/// </remarks>
/// <param name="CashOnDelivery">Kolik dopravce vybere při doručení (dobírka), jinak null.</param>
public sealed record CreateShipmentV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    string RecipientName,
    string Street,
    string City,
    string PostalCode,
    decimal? CashOnDelivery) : IIntegrationCommand;
