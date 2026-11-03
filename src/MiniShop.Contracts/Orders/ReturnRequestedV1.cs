namespace MiniShop.Contracts.Orders;

/// <summary>
/// E-shop krok 12: zákazník chce doručenou objednávku vrátit – fakt pro odběratele (Notifications: „pošlete zboží do…“).
/// </summary>
/// <param name="SendByUtc">Do kdy musí zboží dorazit, jinak vrácení vyprší.</param>
public sealed record ReturnRequestedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CustomerEmail,
    string Reason,
    DateTime SendByUtc) : IIntegrationEvent;
