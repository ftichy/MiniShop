using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;
using MiniShop.Domain.Products;

namespace MiniShop.Domain.Orders;

/// <summary>
/// Objednávka zákazníka. Položky lze přidávat jen přes <see cref="AddItem"/>,
/// stav se mění jen přes <see cref="Complete"/> a <see cref="Cancel"/>.
/// Každá změna stavu vyvolá doménovou událost (OrderPlaced, OrderCompleted, OrderCancelled).
/// </summary>
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
        Status = OrderStatus.Created;

        // Položky se přidají až po konstruktoru, ale událost drží odkaz na objednávku,
        // takže handler (krok 2) uvidí objednávku i s položkami.
        Raise(new OrderPlaced(this, createdAtUtc));
    }

    public int Id { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public OrderStatus Status { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public decimal TotalPrice => _items.Sum(i => i.LineTotal);

    public void AddItem(Product product, int quantity, decimal discount)
    {
        EnsureStatus(OrderStatus.Created, "Položky lze přidávat jen do nové objednávky.");
        _items.Add(new OrderItem(product.Id, product.Name, product.Price, quantity, discount));
    }

    public void Complete(DateTime completedAtUtc)
    {
        EnsureStatus(OrderStatus.Created, "Vyřídit lze jen novou objednávku.");
        Status = OrderStatus.Completed;
        Raise(new OrderCompleted(this, completedAtUtc));
    }

    public void Cancel(DateTime cancelledAtUtc)
    {
        EnsureStatus(OrderStatus.Created, "Zrušit lze jen novou objednávku.");
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
