using MiniShop.Domain.Notifications;

namespace MiniShop.Application.Notifications;

/// <summary>E-shop krok 7: příjemci oznámení (kopie z OrderPlacedV2).</summary>
public interface IOrderRecipientRepository
{
    Task<OrderRecipient?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    void Add(OrderRecipient recipient);

    /// <summary>Zapomenout zákazníka (smazal účet) – smaže všechny jeho řádky i s položkami. Vrací počet objednávek.</summary>
    Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default);
}
