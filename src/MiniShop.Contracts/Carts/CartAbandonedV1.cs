namespace MiniShop.Contracts.Carts;

/// <summary>
/// E-shop krok 10: zákazník nechal v košíku zboží a dlouho (24 h) se nic nestalo – fakt pro odběratele, kteří mu
/// chtějí připomenout nákup (Notifications). Událost nevzniká z akce zákazníka, ale z „ticha“ – najde ji worker.
/// </summary>
/// <remarks>
/// Nenese osobní údaje, jen ID účtu – e-mail a souhlas s připomínkami si odběratel zjistí až při odeslání
/// (souhlas musí platit v tu chvíli, ne v době, kdy zpráva vznikla).
/// </remarks>
/// <param name="Items">Snímek košíku s aktuálními názvy a cenami z kopie katalogu.</param>
public sealed record CartAbandonedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int CartId,
    string CustomerId,
    IReadOnlyList<CartItemV1> Items) : IIntegrationEvent;

/// <summary>Řádek košíku ve snímku <see cref="CartAbandonedV1"/>.</summary>
public sealed record CartItemV1(int ProductId, string ProductName, int Quantity, decimal UnitPrice);
