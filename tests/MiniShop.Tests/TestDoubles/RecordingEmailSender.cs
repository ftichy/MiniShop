using System.Collections.Concurrent;
using MiniShop.Application.Abstractions;

namespace MiniShop.Tests.TestDoubles;

/// <summary>Účty krok 5: „poštovní schránka“ testů – e-maily se neodesílají, jen se zapamatují.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    /// <summary>E-maily pro jednoho adresáta – testy sdílí jeden Web, každý používá vlastní adresu.</summary>
    public IReadOnlyList<EmailMessage> To(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
