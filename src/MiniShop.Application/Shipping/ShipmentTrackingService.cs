using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Shipping;

namespace MiniShop.Application.Shipping;

/// <summary>
/// E-shop krok 26C: stav zásilky od dopravce – jedna cesta pro webhook i dohledání (GET). Duplicitu a pořadí řeší
/// <see cref="Shipment.ApplyTracking"/> podle <c>sequence</c>; doručení zapíše ShipmentDeliveredV1 do outboxu ve stejné
/// transakci.
/// </summary>
public sealed class ShipmentTrackingService(
    IShipmentRepository shipments,
    ICarrierGateway carrier,
    IShippingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Zapíše stavy od dopravce. Souběh (stejná zásilka na dvou kopiích Webu) = <see cref="ConcurrencyConflictException"/>
    /// – webhook odpoví 503 a dopravce ho zopakuje.
    /// </summary>
    public async Task<TrackingApplyResult> ApplyAsync(string trackingNumber, IReadOnlyList<CarrierTrackingUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        var shipment = await shipments.GetByTrackingNumberAsync(trackingNumber, cancellationToken);
        if (shipment is null)
        {
            return TrackingApplyResult.UnknownShipment;
        }

        TraceTags.TagOrder(shipment.OrderId);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var changed = false;
        foreach (var update in updates.OrderBy(u => u.Sequence))
        {
            changed |= shipment.ApplyTracking(update.Sequence, update.Status, update.OccurredAtUtc, now);
        }

        if (!changed)
        {
            return TrackingApplyResult.NoChange;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ShipmentDeliveredV1, je-li doručeno
        TraceTags.Tag("minishop.shipment.status", shipment.Status.ToString());
        return TrackingApplyResult.Applied;
    }

    /// <summary>
    /// Dohledání: zásilky na cestě bez novinky déle než <paramref name="quietFor"/> se zeptá dopravce (GET) a zapíše,
    /// co webhooky nedonesly. Vrací počet zkontrolovaných zásilek. Dočasná nedostupnost dopravce = výjimka (další kolo).
    /// </summary>
    public async Task<int> ReconcileAsync(TimeSpan quietFor, int batchSize, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stale = await shipments.GetQuietAsync(now - quietFor, batchSize, cancellationToken);
        foreach (var shipment in stale)
        {
            TraceTags.TagOrder(shipment.OrderId);
            var tracking = await carrier.GetTrackingAsync(shipment.TrackingNumber!, cancellationToken);
            if (tracking is not null)
            {
                foreach (var update in tracking.OrderBy(u => u.Sequence))
                {
                    shipment.ApplyTracking(update.Sequence, update.Status, update.OccurredAtUtc, now);
                }
            }

            shipment.MarkChecked(now, unknownAtCarrier: tracking is null);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }
}

/// <summary>E-shop krok 26C: jeden stav zásilky od dopravce, už přeložený na naše pojmy.</summary>
public sealed record CarrierTrackingUpdate(int Sequence, ShipmentStatus Status, DateTime OccurredAtUtc);

public enum TrackingApplyResult
{
    Applied,
    NoChange,
    UnknownShipment
}
