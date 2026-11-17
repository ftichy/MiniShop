using System.Collections.Concurrent;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Tests.TestDoubles;

/// <summary>E-shop krok 23: „push služba“ testů – oznámení se neodesílají, jen se zapamatují (na které zařízení a co).</summary>
public sealed class RecordingPushSender : IPushSender
{
    private readonly ConcurrentQueue<(string Endpoint, string CustomerId, PushMessage Message)> _sent = new();
    private readonly ConcurrentDictionary<string, bool> _gone = new();

    public bool IsEnabled { get; set; } = true;

    public string? PublicKey => IsEnabled ? "test-vapid-public-key" : null;

    public IReadOnlyList<(string Endpoint, string CustomerId, PushMessage Message)> Sent => _sent.ToList();

    public IReadOnlyList<PushMessage> To(string customerId) =>
        _sent.Where(s => s.CustomerId == customerId).Select(s => s.Message).ToList();

    /// <summary>Zařízení, které push služba už nezná (404/410).</summary>
    public void MarkGone(string endpoint) => _gone[endpoint] = true;

    /// <summary>Kolikrát má odeslání ještě selhat (výpadek push služby).</summary>
    public int FailNext { get; set; }

    public Task<PushDeliveryResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken = default)
    {
        if (FailNext > 0)
        {
            FailNext--;
            throw new HttpRequestException("Push služba nedostupná");
        }

        if (_gone.ContainsKey(subscription.Endpoint))
        {
            return Task.FromResult(PushDeliveryResult.Gone);
        }

        _sent.Enqueue((subscription.Endpoint, subscription.CustomerId, message));
        return Task.FromResult(PushDeliveryResult.Sent);
    }
}
