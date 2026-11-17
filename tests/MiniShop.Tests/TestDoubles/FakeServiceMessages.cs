using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Web.Messages;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Mikroservisy M2B: zprávy jiné služby (sklad, přehled tržeb) v testech Webu – služby neběží. Má jednu poison zprávu
/// a jednu odloženou zprávu v dead-letter frontě, pamatuje si „Zkusit znovu“ / „Vrátit do fronty“ a umí „spadnout“.
/// </summary>
public sealed class FakeServiceMessages(string serviceName, string module, string queue) : IServiceMessages
{
    public static readonly Guid PoisonId = Guid.Parse("6f1d2c3b-0000-4000-8000-000000000001");

    public bool Available { get; set; } = true;

    public List<(string Module, Guid Id)> Retried { get; } = [];

    public List<string> Requeued { get; } = [];

    public string ServiceName => serviceName;

    public string DisplayName => $"Testovací služba {serviceName}";

    public Task<ServiceMessagesStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        var poison = new PoisonMessage(PoisonId, "ProductUpdatedV2", DateTime.UtcNow, 5, "Testovací chyba");
        return Task.FromResult(new ServiceMessagesStatus(
            [new MessageStoreStatus(module, 0, 1, [poison])],
            new Dictionary<string, uint> { [queue] = 2 }));
    }

    public Task<bool> RetryAsync(string module, Guid messageId, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        Retried.Add((module, messageId));
        return Task.FromResult(true);
    }

    public Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable();
        Requeued.Add(queue);
        return Task.FromResult(2);
    }

    private void ThrowIfUnavailable()
    {
        if (!Available)
        {
            throw new HttpRequestException($"{serviceName} neběží");
        }
    }
}
