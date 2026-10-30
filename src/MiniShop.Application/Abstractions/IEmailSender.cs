namespace MiniShop.Application.Abstractions;

/// <summary>Jeden e-mail. Text je povinný (čtečky bez HTML), HTML volitelné.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// Účty krok 5: odeslání e-mailu. Sdílené jádro – používají ho účty (potvrzení, reset hesla) i objednávky (oznámení).
/// Implementace: SMTP přes MailKit (Gmail v produkci, Mailpit lokálně), bez nastaveného serveru jen zápis do logu.
/// </summary>
/// <remarks>Odeslání je vnější akce – nejde vzít zpět. Při chybě vyhodí výjimku (volající rozhodne, co dál).</remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
