using MiniShop.Application.Abstractions;
using MiniShop.Domain.Shipping;

namespace MiniShop.Application.Shipping;

/// <summary>E-shop krok 26B: zásilky (jeden řádek na příkaz CreateShipmentV1).</summary>
public interface IShipmentRepository
{
    Task<Shipment?> GetByCommandIdAsync(Guid commandId, CancellationToken cancellationToken = default);
    void Add(Shipment shipment);
}

/// <summary>E-shop krok 26B: uložení změn modulu Shipping – zásilky, outbox a inbox ve schématu shipping.</summary>
public interface IShippingUnitOfWork : IUnitOfWork;
