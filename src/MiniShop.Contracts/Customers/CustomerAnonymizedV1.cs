namespace MiniShop.Contracts.Customers;

/// <summary>
/// E-shop krok 7: zákazník smazal účet a jeho objednávky jsou anonymizované. Kdo si z událostí uložil jeho osobní
/// údaje (jméno, e-mail, adresu – Notifications), musí je smazat.
/// </summary>
public sealed record CustomerAnonymizedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CustomerId) : IIntegrationEvent;
