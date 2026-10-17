using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Domain.Orders;

/// <summary>
/// Objednávka zákazníka. Položky lze přidávat jen přes <see cref="AddItem"/>,
/// stav se mění jen přes <see cref="Confirm"/>, <see cref="Reject"/>, <see cref="Complete"/> a <see cref="Cancel"/>.
/// Každá změna stavu vyvolá doménovou událost.
/// </summary>
/// <remarks>
/// Od kroku 9 objednávka vzniká jako Pending a o potvrzení rozhoduje sága (OrderSaga) podle odpovědi skladu.
/// Objednávka sama sklad nezná – jen přijme výsledek.
/// </remarks>
public class Order : Entity
{
    private readonly List<OrderItem> _items = new();

    // Konstruktor pro EF Core. EF při načítání z DB volá tento (bezparametrický) konstruktor,
    // takže se při načtení objednávky událost OrderPlaced znovu nevyvolá.
    private Order()
    {
    }

    public Order(string customerName, string customerEmail, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new DomainException("Jméno zákazníka je povinné.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new DomainException("E-mail zákazníka je povinný.");
        }

        CustomerName = customerName.Trim();
        CustomerEmail = customerEmail.Trim();
        CreatedAtUtc = createdAtUtc;
        Status = OrderStatus.Pending;

        // Položky se přidají až po konstruktoru, ale událost drží odkaz na objednávku,
        // takže handler (krok 2) uvidí objednávku i s položkami.
        Raise(new OrderPlaced(this, createdAtUtc));
    }

    public int Id { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public OrderStatus Status { get; private set; }

    /// <summary>Proč byla objednávka zamítnuta (jen ve stavu Rejected).</summary>
    public string? RejectionReason { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public decimal TotalPrice => _items.Sum(i => i.LineTotal);

    /// <summary>Název a cena se berou z lokální kopie katalogu (krok 10) a uloží se jako snímek v položce.</summary>
    public void AddItem(CatalogProduct product, int quantity, decimal discount)
    {
        EnsureStatus(OrderStatus.Pending, "Položky lze přidávat jen do nové objednávky.");
        if (product.IsRemoved)
        {
            throw new DomainException($"Produkt {product.Name} už není v nabídce.");
        }

        _items.Add(new OrderItem(product.Id, product.Name, product.Price, quantity, discount));
    }

    /// <summary>Sklad zboží zarezervoval (krok 9).</summary>
    public void Confirm(DateTime confirmedAtUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Potvrdit lze jen čekající objednávku.");
        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmed(this, confirmedAtUtc));
    }

    /// <summary>Sklad zboží nezarezervoval nebo neodpověděl včas (krok 9).</summary>
    public void Reject(string reason, DateTime rejectedAtUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Zamítnout lze jen čekající objednávku.");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Důvod zamítnutí je povinný.");
        }

        Status = OrderStatus.Rejected;
        RejectionReason = reason.Trim();
        Raise(new OrderRejected(this, rejectedAtUtc));
    }

    public void Complete(DateTime completedAtUtc)
    {
        EnsureStatus(OrderStatus.Confirmed, "Vyřídit lze jen potvrzenou objednávku.");
        Status = OrderStatus.Completed;
        Raise(new OrderCompleted(this, completedAtUtc));
    }

    /// <summary>Zrušení potvrzené objednávky – sága pak uvolní rezervaci skladu (kompenzace).</summary>
    public void Cancel(DateTime cancelledAtUtc)
    {
        EnsureStatus(OrderStatus.Confirmed, "Zrušit lze jen potvrzenou objednávku.");
        Status = OrderStatus.Cancelled;
        Raise(new OrderCancelled(this, cancelledAtUtc));
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainException(message);
        }
    }
}
