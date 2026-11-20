using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Shipping;

namespace MiniShop.Infrastructure.Shipping;

/// <summary>
/// E-shop krok 26C: webhook dopravce. Pořadí jako u Stripu: <b>nejdřív podpis a čas</b> (HMAC-SHA256 tajemstvím
/// <c>Carrier:WebhookSecret</c> nad „t.tělo“, nejvýš <see cref="Tolerance"/> staré), teprve pak obsah. JSON dopravce
/// zná jen tahle třída a <see cref="CarrierApiClient"/> (překladová vrstva); dál jde <see cref="CarrierTrackingUpdate"/>.
/// </summary>
/// <remarks>
/// Odpovědi: 200 = převzato (i duplicita, pozdní zpráva, cizí zásilka – dopravce nemá opakovat), 401 = podpis/čas,
/// 400 = nečitelné tělo, 503 = souběh (dopravce zopakuje). Do logu jen číslo zásilky, nikdy tělo.
/// </remarks>
public sealed class CarrierWebhookProcessor(
    CarrierWebhookSettings settings,
    ShipmentTrackingService tracking,
    TimeProvider timeProvider,
    ILogger<CarrierWebhookProcessor> logger)
{
    public const string SignatureHeaderName = "Carrier-Signature";
    public const string StatusChangedType = "shipment.status_changed";

    /// <summary>Jak stará smí zpráva být – starší (zachycená a poslaná znovu) se odmítne.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CarrierWebhookResult> ProcessAsync(string body, string? signature, CancellationToken cancellationToken = default)
    {
        if (!IsSignatureValid(body, signature))
        {
            return CarrierWebhookResult.InvalidSignature;   // nic z těla nečíst
        }

        CarrierWebhookJson? message;
        try
        {
            message = JsonSerializer.Deserialize<CarrierWebhookJson>(body, Json);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message?.Data is not { TrackingNumber: { Length: > 0 } trackingNumber } data)
        {
            logger.LogWarning("Webhook dopravce s platným podpisem nejde přečíst");
            return CarrierWebhookResult.Invalid;
        }

        if (message.Type != StatusChangedType || CarrierApiClient.TranslateStatus(data.Status) is not { } status)
        {
            return CarrierWebhookResult.Accepted;   // jiná událost / nový stav, který neznáme – tolerant reader
        }

        try
        {
            var result = await tracking.ApplyAsync(trackingNumber, [new CarrierTrackingUpdate(data.Sequence, status, data.OccurredAt)],
                cancellationToken);
            if (result == TrackingApplyResult.UnknownShipment)
            {
                logger.LogWarning("Webhook dopravce pro neznámou zásilku {TrackingNumber} – ignorován", trackingNumber);
            }

            return CarrierWebhookResult.Accepted;
        }
        catch (ConcurrencyConflictException)
        {
            // Stejnou zásilku právě mění jiný webhook (druhá kopie Webu) – dopravce to za chvíli zkusí znovu.
            return CarrierWebhookResult.RetryLater;
        }
    }

    /// <summary>Hlavička <c>t=&lt;unix&gt;,v1=&lt;hex&gt;</c>; porovnání v konstantním čase.</summary>
    private bool IsSignatureValid(string body, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var parts = signature.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !parts.TryGetValue("v1", out var v1)
            || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
        {
            return false;
        }

        var age = timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (age.Duration() > Tolerance)
        {
            return false;   // stará (přehraná) zpráva nebo hodiny úplně mimo
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.Secret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        byte[] sent;
        try
        {
            sent = Convert.FromHexString(v1);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, sent);
    }

    private sealed record CarrierWebhookJson(string? Id, string? Type, CarrierWebhookData? Data);

    private sealed record CarrierWebhookData(string? TrackingNumber, string? Reference, string? Status, int Sequence, DateTime OccurredAt);
}

public enum CarrierWebhookResult
{
    Accepted,
    InvalidSignature,
    Invalid,
    RetryLater
}

/// <summary>E-shop krok 26C: tajemství podpisu webhooků dopravce (<c>Carrier:WebhookSecret</c>, secret <c>CARRIER_WEBHOOK_SECRET</c>).</summary>
public sealed class CarrierWebhookSettings
{
    public const string ConfigurationKey = "Carrier:WebhookSecret";

    public CarrierWebhookSettings(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                $"Chybí {ConfigurationKey} (aspoň 32 znaků) – Web se nespustí. V produkci secret CARRIER_WEBHOOK_SECRET.");
        }

        Secret = secret;
    }

    public string Secret { get; }
}
