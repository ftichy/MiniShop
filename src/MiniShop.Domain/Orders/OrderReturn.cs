using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 22: vratka po částech – zákazník vrací jen vybrané kusy doručené objednávky. Patří do agregátu
/// <see cref="Order"/> (pravidla „nevrátit víc, než se koupilo“ hlídá objednávka); objednávka zůstává doručená.
/// Vratka celé objednávky najednou dál mění stav objednávky (<see cref="OrderStatus.ReturnRequested"/>) – ta sem nepatří.
/// </summary>
/// <remarks>Co dál (převzetí, naskladnění, peníze), řídí sága vratky – klíč objednávka + <see cref="Number"/>.</remarks>
public sealed class OrderReturn
{
    public const int ReasonMaxLength = 500;

    private readonly List<OrderReturnLine> _lines = new();

    // Konstruktor pro EF Core.
    private OrderReturn()
    {
    }

    public int Id { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>Číslo vratky v objednávce (1, 2, …) – sklad, Payments i Invoicing ho znají jako <c>ReturnNumber</c>.</summary>
    public int Number { get; private set; }

    public OrderReturnStatus Status { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Kdy se vratka vyřídila (dokončena nebo zamítnuta), nebo null.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>Proč vratka neprošla (admin, vypršení), nebo null.</summary>
    public string? RejectionReason { get; private set; }

    public IReadOnlyCollection<OrderReturnLine> Lines => _lines.AsReadOnly();

    /// <summary>Kolik se vrací – vrácené řádky (doprava se u vratky po částech nevrací).</summary>
    public decimal Amount => _lines.Sum(l => l.LineTotal);

    /// <summary>Čeká se na zboží, naskladnění nebo peníze.</summary>
    public bool IsOpen => Status == OrderReturnStatus.Requested;

    /// <summary>Počítá se do vrácených (nebo právě vracených) kusů – zamítnutá ne.</summary>
    public bool CountsAsReturned => Status != OrderReturnStatus.Rejected;

    internal static OrderReturn Request(int number, string reason, IEnumerable<OrderReturnLine> lines, DateTime requestedAtUtc)
    {
        var orderReturn = new OrderReturn
        {
            Number = number,
            Status = OrderReturnStatus.Requested,
            Reason = reason.Trim()[..Math.Min(reason.Trim().Length, ReasonMaxLength)],
            RequestedAtUtc = requestedAtUtc
        };
        orderReturn._lines.AddRange(lines);
        if (orderReturn._lines.Count == 0)
        {
            throw new DomainException("Vyberte prosím aspoň jeden kus, který vracíte.");
        }

        return orderReturn;
    }

    internal void Complete(DateTime completedAtUtc)
    {
        EnsureOpen("Dokončit lze jen čekající vratku.");
        Status = OrderReturnStatus.Completed;
        ResolvedAtUtc = completedAtUtc;
    }

    internal void Reject(string reason, DateTime rejectedAtUtc)
    {
        EnsureOpen("Zamítnout lze jen čekající vratku.");
        Status = OrderReturnStatus.Rejected;
        RejectionReason = reason;
        ResolvedAtUtc = rejectedAtUtc;
    }

    private void EnsureOpen(string message)
    {
        if (!IsOpen)
        {
            throw new DomainException(message);
        }
    }
}

/// <summary>E-shop krok 22: vrácené kusy jednoho produktu – ceny z objednávky, sleva z kuponu poměrně na kusy.</summary>
public sealed record OrderReturnLine(int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Discount)
{
    public decimal LineTotal => UnitPrice * Quantity - Discount;
}

/// <summary>E-shop krok 22: stav vratky po částech. Ukládá se jako text – nové hodnoty jen na konec.</summary>
public enum OrderReturnStatus
{
    Requested,
    Completed,
    Rejected
}
