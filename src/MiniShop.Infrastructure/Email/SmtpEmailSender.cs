using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MiniShop.Application.Abstractions;

namespace MiniShop.Infrastructure.Email;

/// <summary>
/// Účty krok 5: odeslání přes SMTP (MailKit). Pro každý e-mail nové spojení – MiniShop posílá málo e-mailů
/// a nemusí řešit výpadky dlouho otevřeného spojení.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        foreach (var attachment in message.Attachments)   // e-shop krok 15: faktura v PDF
        {
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        // Host je vyplněný – bez něj AddEmail SmtpEmailSender vůbec nezaregistruje.
        await client.ConnectAsync(settings.Host!, settings.Port,
            settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        // Adresát je osobní údaj, obsah může nést odkaz s tokenem – do logu jen předmět.
        logger.LogInformation("E-mail „{Subject}“ odeslán", message.Subject);
    }
}

/// <summary>
/// Účty krok 5: náhrada bez SMTP serveru (testy, lokální Compose) – e-mail se neodešle, jen se zaloguje.
/// Text (s odkazy) jen na úrovni Debug, aby se tokeny nedostaly do běžných logů.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("E-mail „{Subject}“ se neodeslal – SMTP server není nastavený (sekce Email:Host)",
            message.Subject);
        logger.LogDebug("Text e-mailu: {Body}", message.TextBody);
        foreach (var attachment in message.Attachments)
        {
            logger.LogDebug("Příloha {FileName} ({Size} B)", attachment.FileName, attachment.Content.Length);
        }

        return Task.CompletedTask;
    }
}
