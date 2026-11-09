namespace MiniShop.Contracts.Invoicing;

/// <summary>
/// E-shop krok 15B: modul Invoicing vystavil dobropis – zaplacená objednávka s fakturou byla zrušená, zamítnutá
/// nebo vrácená. Čte Notifications (e-mail s PDF dobropisu v příloze). PDF ve zprávě není – jen čísla.
/// </summary>
/// <param name="CreditNoteNumber">Číslo dobropisu z vlastní řady roku, např. „D2026000001“.</param>
/// <param name="InvoiceNumber">Číslo opravované faktury.</param>
/// <param name="Amount">Kolik se vrací (celá částka faktury, kladné číslo).</param>
public sealed record CreditNoteIssuedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string CreditNoteNumber,
    string InvoiceNumber,
    decimal Amount) : IIntegrationEvent;
