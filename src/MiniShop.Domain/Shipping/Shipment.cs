using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Shipping;

/// <summary>
/// E-shop krok 26B: zásilka u dopravce – jeden pokus o předání objednávky (jeden příkaz CreateShipmentV1). Po odmítnutí
/// může skladník zkusit znovu = nový příkaz = nová zásilka; vytvořená (<see cref="ShipmentStatus.Created"/>) je
/// k objednávce nejvýš jedna (filtrovaný unikátní index).
/// </summary>
/// <remarks>
/// Modul Shipping objednávky nezná – jen číslo objednávky z příkazu. Adresu si neukládá (zná ji dopravce i objednávka).
/// </remarks>
public class Shipment : Entity
{
    public const int CarrierMaxLength = 50;
    public const int TrackingNumberMaxLength = 50;
    public const int FailureMaxLength = 300;

    // Konstruktor pro EF Core.
    private Shipment()
    {
    }

    private Shipment(Guid commandId, int orderId, DateTime atUtc)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Zásilka potřebuje číslo objednávky.");
        }

        Id = Guid.NewGuid();
        CommandId = commandId;
        OrderId = orderId;
        RequestedAtUtc = atUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>ID příkazu CreateShipmentV1 = Idempotency-Key u dopravce (unikátní).</summary>
    public Guid CommandId { get; private set; }

    public int OrderId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public string? Carrier { get; private set; }

    public string? TrackingNumber { get; private set; }

    /// <summary>Proč dopravce zásilku odmítl (česky), jen u <see cref="ShipmentStatus.Failed"/>.</summary>
    public string? FailureReason { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Dopravce zásilku přijal a přidělil číslo → ShipmentCreated (→ ShipmentCreatedV1).</summary>
    public static Shipment Created(Guid commandId, int orderId, string carrier, string trackingNumber, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber)
            || carrier.Length > CarrierMaxLength || trackingNumber.Length > TrackingNumberMaxLength)
        {
            throw new DomainException("Dopravce vrátil neplatné číslo zásilky.");
        }

        var shipment = new Shipment(commandId, orderId, atUtc)
        {
            Status = ShipmentStatus.Created,
            Carrier = carrier,
            TrackingNumber = trackingNumber
        };
        shipment.Raise(new Events.ShipmentCreated(shipment, atUtc));
        return shipment;
    }

    /// <summary>Dopravce zásilku odmítl (trvalá chyba) → ShipmentFailed (→ ShipmentFailedV1).</summary>
    public static Shipment Failed(Guid commandId, int orderId, string reason, DateTime atUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? "Dopravce zásilku odmítl." : reason.Trim();
        var shipment = new Shipment(commandId, orderId, atUtc)
        {
            Status = ShipmentStatus.Failed,
            FailureReason = trimmed.Length > FailureMaxLength ? trimmed[..FailureMaxLength] : trimmed
        };
        shipment.Raise(new Events.ShipmentFailed(shipment, atUtc));
        return shipment;
    }
}

/// <summary>E-shop krok 26B: stav zásilky v našem modulu (ne slovník dopravce). Nové hodnoty jen na konec (ukládá se text).</summary>
public enum ShipmentStatus
{
    Created,
    Failed
}
