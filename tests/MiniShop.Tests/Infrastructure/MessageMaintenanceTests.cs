using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Inbox;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Ordering;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Tests.TestDoubles;
using RabbitMQ.Client;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// EDA krok 13: úklid outboxu a inboxu (co se smaže a co nikdy), ruční opakování poison zprávy a pravidla
/// opakování v RabbitMQ (počet pokusů v hlavičce, backoff, dead-letter fronta).
/// </summary>
public sealed class MessageMaintenanceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabases _databases = new();   // škálování krok 2 a 3C: PostgreSQL (Testcontainers)
    private readonly FixedTimeProvider _time = new(Now);
    private readonly ServiceProvider _services;

    public MessageMaintenanceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(_time);
        services.AddInfrastructure(_databases.Ordering, _databases.Inventory);
        _services = services.BuildServiceProvider();
        OrderingDatabaseInitializer.InitializeAsync(_services).GetAwaiter().GetResult();
    }

    private IMessageStore Store => _services.GetServices<IMessageStore>().Single(s => s.ModuleName == "Ordering");

    [Fact]
    public async Task Uklid_MazeJenStareZpracovane_PoisonANezpracovaneNikdy()
    {
        await Db(db =>
        {
            db.OutboxMessages.AddRange(
                Message(processedDaysAgo: 8),                    // starý zpracovaný → pryč
                Message(processedDaysAgo: 2),                    // čerstvý zpracovaný → zůstane
                Message(processedDaysAgo: null, attempts: 0),    // nezpracovaný → zůstane
                Message(processedDaysAgo: null, attempts: OutboxProcessor.MaxAttempts));   // poison → zůstane
            db.ProcessedMessages.AddRange(
                new ProcessedMessage { EventId = Guid.NewGuid(), Handler = "A", ProcessedAtUtc = Now.AddDays(-31) },
                new ProcessedMessage { EventId = Guid.NewGuid(), Handler = "A", ProcessedAtUtc = Now.AddDays(-5) });
            return db.SaveChangesAsync();
        });

        var worker = ActivatorUtilities.CreateInstance<MessageRetentionWorker>(_services,
            new MessageRetentionSettings(TimeSpan.FromDays(7), TimeSpan.FromDays(30)));
        await worker.CleanUpAsync(CancellationToken.None);

        Assert.Equal(3, await Db(db => db.OutboxMessages.CountAsync()));
        Assert.Equal(1, await Db(db => db.ProcessedMessages.CountAsync()));
        Assert.Equal(0, await Db(db => db.OutboxMessages.CountAsync(m => m.ProcessedAtUtc < Now.AddDays(-7))));
    }

    [Fact]
    public async Task Poison_PrehledAZkusitZnovu()
    {
        var poison = Message(processedDaysAgo: null, attempts: OutboxProcessor.MaxAttempts);
        poison.Error = "SMTP nedostupné";
        await Db(db => { db.OutboxMessages.AddRange(poison, Message(processedDaysAgo: null, attempts: 1)); return db.SaveChangesAsync(); });

        var status = await Store.GetStatusAsync();
        Assert.Equal((1, 1), (status.Pending, status.PoisonCount));
        Assert.Equal("SMTP nedostupné", Assert.Single(status.Poison).Error);

        Assert.True(await Store.RetryAsync(poison.Id));
        Assert.Equal((2, 0), ((await Store.GetStatusAsync()).Pending, (await Store.GetStatusAsync()).PoisonCount));
        Assert.False(await Store.RetryAsync(Guid.NewGuid()));
    }

    [Fact]
    public void Backoff_OutboxIFronta_RostePauza()
    {
        Assert.Equal([5, 10, 20, 40], Enumerable.Range(1, 4).Select(a => OutboxProcessor.RetryDelay(a).TotalSeconds));
        Assert.Equal([2, 4, 8, 16], Enumerable.Range(1, 4).Select(a => RabbitMqDeadLetters.RetryDelay(a).TotalSeconds));
    }

    [Fact]
    public void Fronta_PocetPokusuVHlavicce_ChybaJenUOdlozene()
    {
        var original = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "OrderPlacedV2",
            Headers = new Dictionary<string, object?> { ["traceparent"] = "00-abc-def-01" }
        };
        Assert.Equal(0, RabbitMqDeadLetters.ReadAttempts(original));

        var retry = RabbitMqDeadLetters.CopyProperties(original, attempts: 2);
        Assert.Equal(2, RabbitMqDeadLetters.ReadAttempts(retry));
        Assert.Equal((original.MessageId, original.Type), (retry.MessageId, retry.Type));
        Assert.Equal("00-abc-def-01", retry.Headers!["traceparent"]);   // trasa pokračuje i po opakování

        var dead = RabbitMqDeadLetters.CopyProperties(retry, attempts: 5, error: "Handler selhal");
        Assert.Equal("Handler selhal", dead.Headers![RabbitMqDeadLetters.ErrorHeader]);

        var requeued = RabbitMqDeadLetters.CopyProperties(dead, attempts: 0);   // admin vrátil do fronty
        Assert.False(requeued.Headers!.ContainsKey(RabbitMqDeadLetters.ErrorHeader));
        Assert.Equal("minishop.ordering.dead", RabbitMqDeadLetters.DeadQueueFor("minishop.ordering"));
    }

    private static OutboxMessage Message(int? processedDaysAgo, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        Kind = OutboxMessageKind.Domain,
        Type = "OrderPlaced",
        Payload = "{}",
        OccurredAtUtc = Now.AddDays(-(processedDaysAgo ?? 0) - 1),
        ProcessedAtUtc = processedDaysAgo is { } days ? Now.AddDays(-days) : null,
        Attempts = attempts
    };

    private async Task<T> Db<T>(Func<OrderingDbContext, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderingDbContext>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _databases.Dispose();
    }
}
