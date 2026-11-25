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
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<CreateShipmentV1>,
    IIntegrationMessageHandler<CreateShipmentV2>
{
    /// <summary>Starší příkaz (celá objednávka) = balík 1 – může ležet ve frontě z doby před krokem 28C.</summary>
    public Task HandleAsync(CreateShipmentV1 command, CancellationToken cancellationToken) =>
        CreateAsync(command.CommandId, command.OrderId, parcelNumber: 1,
            new CarrierShipmentRequest(command.OrderId, command.RecipientName, command.Street, command.City, command.PostalCode,
                command.CashOnDelivery), cancellationToken);

    /// <summary>E-shop krok 28C: jeden balík objednávky – vlastní příkaz, vlastní Idempotency-Key, vlastní zásilka.</summary>
    public Task HandleAsync(CreateShipmentV2 command, CancellationToken cancellationToken)
    {
        TraceTags.Tag("minishop.shipment.parcel", command.ParcelNumber);
        return CreateAsync(command.CommandId, command.OrderId, command.ParcelNumber,
            new CarrierShipmentRequest(command.OrderId, command.RecipientName, command.Street, command.City, command.PostalCode,
                command.CashOnDelivery), cancellationToken);
    }

    private async Task CreateAsync(Guid commandId, int orderId, int parcelNumber, CarrierShipmentRequest request,
        CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(orderId);
        if (await shipments.GetByCommandIdAsync(commandId, cancellationToken) is not null)
        {
            return;   // tento příkaz už zpracovaný (pojistka vedle inboxu)
        }

        var result = await carrier.CreateShipmentAsync(request, commandId, cancellationToken);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var shipment = result switch
        {
            CarrierShipmentAccepted accepted => Shipment.Created(commandId, orderId, accepted.Carrier, accepted.TrackingNumber, now, parcelNumber),
            CarrierShipmentRejected rejected => Shipment.Failed(commandId, orderId, rejected.Reason, now, parcelNumber),
            _ => throw new InvalidOperationException($"Neznámý výsledek dopravce {result.GetType().Name}.")
        };

        shipments.Add(shipment);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → ShipmentCreatedV1+V2 / ShipmentFailedV1+V2 (outbox, stejná transakce)
        TraceTags.Tag("minishop.shipment.result", shipment.Status == ShipmentStatus.Created ? "created" : "failed");
    }
}
