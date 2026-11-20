using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Shipping;

namespace MiniShop.Application.Shipping;

/// <summary>
/// E-shop krok 26B: modul Shipping – předá zásilku dopravci a výsledek oznámí událostí (ShipmentCreatedV1 / ShipmentFailedV1).
/// </summary>
/// <remarks>
/// Pořadí: nejdřív dopravce, pak náš zápis. Když spadne zápis po úspěšném volání (pád, restart), consumer zprávu zopakuje
/// a dopravce díky Idempotency-Key (= ID příkazu) vrátí TUTÉŽ zásilku – inbox chrání jen naši stranu, klíč i dopravce.
/// Dočasná chyba dopravce (<see cref="CarrierUnavailableException"/>) projde ven → opakování; trvalá → ShipmentFailedV1.
/// </remarks>
public sealed class CreateShipmentHandler(
    IShipmentRepository shipments,
    ICarrierGateway carrier,
    IShippingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IIntegrationMessageHandler<CreateShipmentV1>
{
    public async Task HandleAsync(CreateShipmentV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        if (await shipments.GetByCommandIdAsync(command.CommandId, cancellationToken) is not null)
        {
            return;   // tento příkaz už zpracovaný (pojistka vedle inboxu)
        }

        var result = await carrier.CreateShipmentAsync(
            new CarrierShipmentRequest(command.OrderId, command.RecipientName, command.Street, command.City, command.PostalCode,
                command.CashOnDelivery),
            command.CommandId, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var shipment = result switch
        {
            CarrierShipmentAccepted accepted => Shipment.Created(command.CommandId, command.OrderId, accepted.Carrier, accepted.TrackingNumber, now),
            CarrierShipmentRejected rejected => Shipment.Failed(command.CommandId, command.OrderId, rejected.Reason, now),
            _ => throw new InvalidOperationException($"Neznámý výsledek dopravce {result.GetType().Name}.")
        };

        shipments.Add(shipment);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ShipmentCreatedV1 / ShipmentFailedV1 (outbox, stejná transakce)
        TraceTags.Tag("minishop.shipment.result", shipment.Status == ShipmentStatus.Created ? "created" : "failed");
    }
}
