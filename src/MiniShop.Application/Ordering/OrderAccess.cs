using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>
/// Účty krok 3: kdo se na objednávky ptá. Zákazník vidí jen své, správce všechny.
/// </summary>
/// <remarks>
/// OrderService ho dostává výslovně parametrem – aplikační vrstva o HTTP ani přihlášení nic neví
/// (převod z přihlášeného uživatele dělá Web). Ordering zná jen ID zákazníka, ne modul účtů.
/// </remarks>
public sealed record OrderAccess
{
    private OrderAccess(string? customerId) => CustomerId = customerId;

    /// <summary>Správce – vidí všechny objednávky.</summary>
    public static OrderAccess AllOrders { get; } = new((string?)null);

    /// <summary>Zákazník – vidí jen objednávky se svým ID.</summary>
    public static OrderAccess Customer(string customerId) =>
        string.IsNullOrWhiteSpace(customerId)
            ? throw new ArgumentException("ID zákazníka je povinné.", nameof(customerId))
            : new(customerId);

    /// <summary>ID zákazníka, nebo null = všechny objednávky.</summary>
    public string? CustomerId { get; }

    public bool CanSee(Order order) => CustomerId is null || order.BelongsTo(CustomerId);
}

/// <summary>
/// Účty krok 3: kdo objednává – z přihlášeného účtu, ne z formuláře (e-mail tak nejde podvrhnout).
/// Jméno a e-mail se na objednávku zkopírují.
/// </summary>
public sealed record OrderCustomer(string CustomerId, string Name, string Email);
