using MiniShop.Application.Abstractions;
using MiniShop.Domain.Shipping;

namespace MiniShop.Application.Shipping;

/// <summary>E-shop krok 26B: zásilky (jeden řádek na příkaz CreateShipmentV1).</summary>
public interface IShipmentRepository
{
    Task<Shipment?> GetByCommandIdAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 26C: zásilka podle čísla od dopravce (webhook) – i s historií stavů.</summary>
    Task<Shipment?> GetByTrackingNumberAsync(string trackingNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 26C: zásilky na cestě, o kterých od <paramref name="quietSinceUtc"/> nic nepřišlo a které se od té doby
    /// ani nedohledávaly – nejstarší první, nejvýš <paramref name="batchSize"/>. E-shop krok 26E: navíc ukončené zásilky
    /// s dírou v historii (ztracený webhook), které se od poslední novinky ještě nedohledávaly – jednou.
    /// </summary>
    Task<IReadOnlyList<Shipment>> GetQuietAsync(DateTime quietSinceUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 26C: vytvořené (ne odmítnuté) zásilky objednávek s historií – pro stránky.</summary>
    Task<IReadOnlyList<Shipment>> GetCreatedByOrderIdsAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);

    void Add(Shipment shipment);
}

/// <summary>E-shop krok 26B: uložení změn modulu Shipping – zásilky, outbox a inbox ve schématu shipping.</summary>
public interface IShippingUnitOfWork : IUnitOfWork;
