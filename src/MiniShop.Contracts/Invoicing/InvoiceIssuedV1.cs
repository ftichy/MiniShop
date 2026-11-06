namespace MiniShop.Contracts.Invoicing;

/// <summary>
/// E-shop krok 15: modul Invoicing vystavil fakturu k zaplacené objednávce. Čte Notifications (e-mail s PDF v příloze).
/// PDF ve zprávě není (velké) – kdo ho potřebuje, vezme si ho podle čísla objednávky.
/// </summary>
/// <param name="InvoiceNumber">Číslo faktury z nepřerušené řady roku, např. „2026000001“.</param>
public sealed record InvoiceIssuedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string InvoiceNumber,
    decimal Total) : IIntegrationEvent;
