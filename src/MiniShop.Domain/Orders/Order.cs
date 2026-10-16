using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Products;

namespace MiniShop.Domain.Orders;

/// <summary>
/// Objednávka zákazníka. Položky lze přidávat jen přes <see cref="AddItem"/>,
/// stav se mění jen přes <see cref="Complete"/> a <see cref="Cancel"/>.
/// </summary>
public class Order
{
    private readonly List<OrderItem> _items = new();

    // Konstruktor pro EF Core.
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

    public void Complete()
    {
        EnsureStatus(OrderStatus.Created, "Vyřídit lze jen novou objednávku.");
        Status = OrderStatus.Completed;
    }

    public void Cancel()
    {
        EnsureStatus(OrderStatus.Created, "Zrušit lze jen novou objednávku.");
        Status = OrderStatus.Cancelled;
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainException(message);
        }
    }
}
