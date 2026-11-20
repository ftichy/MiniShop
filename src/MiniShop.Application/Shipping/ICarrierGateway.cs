namespace MiniShop.Application.Shipping;

/// <summary>
/// E-shop krok 26B: dopravce z pohledu MiniShopu. Implementace (Infrastructure, <c>CarrierApiClient</c>) je jediné místo,
/// které zná API a slovník dopravce – sem se překládá na naše pojmy (anti-corruption layer).
/// </summary>
public interface ICarrierGateway
{
    /// <summary>
    /// Založí zásilku. <paramref name="idempotencyKey"/> = ID příkazu: opakování se stejným klíčem vrátí tutéž zásilku.
    /// Dočasná nedostupnost dopravce = <see cref="CarrierUnavailableException"/> (zopakovat), trvalé odmítnutí =
    /// <see cref="CarrierShipmentRejected"/>.
    /// </summary>
    Task<CarrierShipmentResult> CreateShipmentAsync(CarrierShipmentRequest request, Guid idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>Zásilka, jak ji MiniShop předává dopravci.</summary>
public sealed record CarrierShipmentRequest(
    int OrderId, string RecipientName, string Street, string City, string PostalCode, decimal? CashOnDelivery);

/// <summary>Výsledek předání: přijato s číslem, nebo trvale odmítnuto.</summary>
public abstract record CarrierShipmentResult;

/// <summary>Dopravce zásilku přijal (<paramref name="Carrier"/> = jméno pro zákazníka a e-mail).</summary>
public sealed record CarrierShipmentAccepted(string Carrier, string TrackingNumber) : CarrierShipmentResult;

/// <summary>Dopravce zásilku odmítl a opakování nepomůže (<paramref name="Reason"/> = česky pro skladníka).</summary>
public sealed record CarrierShipmentRejected(string Reason) : CarrierShipmentResult;

/// <summary>
/// Dopravce teď neodpovídá (503, timeout, síť). Handler výjimku nechá projít → consumer RabbitMQ zprávu zopakuje
/// (5×, pak dead-letter) – se stejným ID příkazu, takže u dopravce nevznikne druhá zásilka.
/// </summary>
public sealed class CarrierUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
