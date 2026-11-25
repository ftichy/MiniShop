namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 28C (směr C): předat dopravci <b>jeden balík</b> objednávky. Jako <see cref="CreateShipmentV1"/>, navíc číslo
/// balíku a sklad, ze kterého balík odchází. Každý balík = vlastní příkaz = vlastní Idempotency-Key = vlastní zásilka.
/// Odpověď: <see cref="ShipmentCreatedV2"/> / <see cref="ShipmentFailedV2"/>.
/// </summary>
/// <remarks>Nese osobní údaje (jméno a adresu příjemce) – jdou dál dopravci.</remarks>
/// <param name="ParcelNumber">Číslo balíku v objednávce (1, 2, …).</param>
/// <param name="SenderWarehouse">Název skladu-odesílatele (pro dopravce a pro přehled).</param>
/// <param name="CashOnDelivery">Kolik dopravce vybere u tohoto balíku (dobírka), jinak null.</param>
public sealed record CreateShipmentV2(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ParcelNumber,
    string SenderWarehouse,
    string RecipientName,
    string Street,
    string City,
    string PostalCode,
    decimal? CashOnDelivery) : IIntegrationCommand;
