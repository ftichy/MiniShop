using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Inventory;

namespace MiniShop.Application.Inventory;

/// <summary>
/// Modul Inventory: vyřídí příkaz <see cref="SyncCatalogV1"/> – pošle znovu aktuální stav všech produktů
/// jako <see cref="ProductUpdatedV1"/> a <see cref="ProductUpdatedV2"/>. Příjemci (kopie katalogu v Orderingu) si podle nich doplní, co jim chybí.
/// </summary>
/// <remarks>
/// Každá zpráva má nové EventId (je to nové odeslání, inbox příjemce ji nesmí zahodit jako duplicitu),
/// ale čas změny je skutečný čas poslední změny produktu (<c>Product.ChangedAtUtc</c>), ne "teď".
/// Kopie katalogu tak starší stav nikdy nepřepíše přes novější – opakovaná obnova je bezpečná.
/// Smazané produkty ve skladu už nejsou, takže je obnova neposílá (kopie je má označené jako odebrané z ProductRemovedV1).
/// </remarks>
public sealed class SyncCatalogHandler(
    IProductRepository products,
    IInventoryMessagePublisher publisher,
    IInventoryUnitOfWork unitOfWork) : IIntegrationMessageHandler<SyncCatalogV1>
{
    public async Task HandleAsync(SyncCatalogV1 command, CancellationToken cancellationToken)
    {
        foreach (var product in await products.GetAllAsync(cancellationToken))
        {
            publisher.Publish(new ProductUpdatedV1(Guid.NewGuid(), product.ChangedAtUtc, product.Id, product.Name, product.Price));

            // E-shop krok 2: V2 nese i popis, kategorii a dostupnost (s časem její poslední změny) – obnova tak doplní výlohu.
            publisher.Publish(product.ToProductUpdatedV2(Guid.NewGuid(), product.ChangedAtUtc));
        }

        // Zprávy jdou přes outbox skladu – odejdou, i kdyby broker zrovna nebyl dostupný.
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
