using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Shipping;

namespace MiniShop.Infrastructure.Shipping;

/// <summary>
/// E-shop krok 26B: klient API dopravce (simulátor <c>carrier-sim</c>) – překladová vrstva (anti-corruption layer).
/// Jediné místo, které zná JSON a kódy chyb dopravce; ven jdou jen naše pojmy (<see cref="CarrierShipmentResult"/>).
/// </summary>
/// <remarks>
/// HttpClient registruje Web (adresa <c>https+http://carrier-sim</c>, klíč <c>X-Api-Key</c>, resilience ze ServiceDefaults –
/// opakování i POST je bezpečné díky Idempotency-Key). Do logu jen číslo objednávky a zásilky, nikdy adresa.
/// </remarks>
public sealed class CarrierApiClient(HttpClient http, ILogger<CarrierApiClient> logger) : ICarrierGateway
{
    /// <summary>Jméno dopravce pro zákazníka (e-mail o odeslání, detail objednávky).</summary>
    public const string CarrierName = "Simulovaný dopravce";

    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public async Task<CarrierShipmentResult> CreateShipmentAsync(CarrierShipmentRequest request, Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/shipments")
        {
            Content = JsonContent.Create(new
            {
                reference = request.OrderId.ToString(CultureInfo.InvariantCulture),
                recipient = new { name = request.RecipientName, street = request.Street, city = request.City, postalCode = request.PostalCode },
                cashOnDelivery = request.CashOnDelivery
            })
        };
        message.Headers.Add(IdempotencyKeyHeader, idempotencyKey.ToString());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTransient(ex))
        {
            throw new CarrierUnavailableException($"Dopravce neodpovídá ({ex.GetType().Name}).", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var created = await response.Content.ReadFromJsonAsync<CarrierShipmentJson>(cancellationToken)
                    ?? throw new CarrierUnavailableException("Dopravce vrátil prázdnou odpověď.");
                logger.LogInformation("Dopravce převzal zásilku objednávky {OrderId}: {TrackingNumber}{Replayed}", request.OrderId,
                    created.TrackingNumber, response.Headers.Contains("Idempotent-Replayed") ? " (opakovaný požadavek – stejná zásilka)" : "");
                return new CarrierShipmentAccepted(CarrierName, created.TrackingNumber);
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict:
                    logger.LogWarning("Dopravce odmítl zásilku objednávky {OrderId}: {Code}", request.OrderId, error.Code);
                    return new CarrierShipmentRejected(Translate(error.Code));
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    // Chyba nastavení (klíč), ne zásilky – opakovat a po 5 pokusech do dead-letter, odkud ji jde vrátit.
                    throw new CarrierUnavailableException(
                        $"Dopravce odmítl klíč e-shopu ({(int)response.StatusCode}) – zkontrolujte Carrier:ApiKey.");
                default:
                    throw new CarrierUnavailableException($"Dopravce odpověděl {(int)response.StatusCode} ({error.Code}).");
            }
        }
    }

    /// <summary>Kódy chyb dopravce → česká hláška pro skladníka (bez osobních údajů).</summary>
    public static string Translate(string? code) => code switch
    {
        "invalid_postal_code" => "Dopravce odmítl PSČ doručovací adresy.",
        "address_not_serviceable" => "Dopravce na tuto adresu nedoručuje.",
        "parcel_too_heavy" => "Dopravce odmítl hmotnost zásilky.",
        "invalid_request" => "Dopravce odmítl údaje zásilky (jméno nebo adresa).",
        "idempotency_key_reused" => "Dopravce hlásí konflikt požadavku – předejte zásilku znovu.",
        _ => $"Dopravce zásilku odmítl ({code ?? "bez kódu"})."
    };

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or TimeoutException
        || ex.GetType().Namespace?.StartsWith("Polly", StringComparison.Ordinal) == true;   // otevřený circuit breaker

    private static async Task<CarrierErrorJson> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<CarrierErrorJson>(cancellationToken) ?? new CarrierErrorJson(null, null);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or InvalidOperationException)
        {
            return new CarrierErrorJson(null, null);
        }
    }

    private sealed record CarrierShipmentJson(string TrackingNumber, string Status);

    private sealed record CarrierErrorJson(string? Code, string? Message);
}
