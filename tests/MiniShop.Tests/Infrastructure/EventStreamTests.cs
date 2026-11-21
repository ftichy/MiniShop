using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Infrastructure.Integration.RabbitMq;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace MiniShop.Tests.Infrastructure;

/// <summary>Jeden RabbitMQ v kontejneru pro testy proudu (Testcontainers) – chování proudu v paměti napodobit nejde.</summary>
public sealed class RabbitMqTestServer : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder().WithImage("rabbitmq:4-management").Build();

    public RabbitMqConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Connection = new RabbitMqConnection(new RabbitMqOptions { ConnectionString = _container.GetConnectionString() },
            NullLogger<RabbitMqConnection>.Instance);
        await RabbitMqEventStream.SetupAsync(Connection, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Proud B1: proud zpráv <c>minishop.events</c> proti skutečnému RabbitMQ – vznik s retencí, zprávy ze všech exchange,
/// čtení od času, opakované čtení (nic nezmizí), filtr objednávky a maskování osobních údajů.
/// </summary>
public sealed class EventStreamTests(RabbitMqTestServer rabbit) : IClassFixture<RabbitMqTestServer>
{
    private RabbitMqEventStreamReader Reader => new(rabbit.Connection, NullLogger<RabbitMqEventStreamReader>.Instance);

    [Fact]
    public async Task ZpravyZExchange_JsouVProudu_CteniJeNemaze()
    {
        var from = DateTime.UtcNow.AddSeconds(-1);
        await PublishAsync(new OrderConfirmedV1(Guid.NewGuid(), DateTime.UtcNow, 9101));
        await PublishAsync(new OrderCompletedV1(Guid.NewGuid(), DateTime.UtcNow, 9101, "jan@example.com"));
        await PublishAsync(new OrderConfirmedV1(Guid.NewGuid(), DateTime.UtcNow, 9102));

        var first = await Reader.ReadAsync(new EventStreamQuery(from, OrderId: 9101));
        var second = await Reader.ReadAsync(new EventStreamQuery(from, OrderId: 9101));   // proud se čtením nemění

        Assert.Equal(["OrderConfirmedV1", "OrderCompletedV1"], first.Entries.Select(e => e.Type));   // pořadí jako v proudu
        Assert.Equal(first.Entries.Select(e => e.Offset), second.Entries.Select(e => e.Offset));
        Assert.True(first.Entries[0].Offset < first.Entries[1].Offset);
        Assert.All(first.Entries, e => Assert.Equal(9101, e.OrderId));
        Assert.DoesNotContain("jan@example.com", first.Entries[1].Payload);   // osobní údaj (OrderCompletedV1) zamaskovaný
        Assert.Contains("•••", first.Entries[1].Payload);
        Assert.Contains("9101", first.Entries[0].Payload);
    }

    [Fact]
    public async Task CteniOdCasu_StarsiZpravyNevrati_FiltrTypu()
    {
        await PublishAsync(new OrderConfirmedV1(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-30), 9201));
        var from = DateTime.UtcNow.AddSeconds(-1);
        await PublishAsync(new OrderCancelledV1(Guid.NewGuid(), DateTime.UtcNow, 9202, "y@example.com", []));
        await PublishAsync(new OrderConfirmedV1(Guid.NewGuid(), DateTime.UtcNow, 9203));

        var all = await Reader.ReadAsync(new EventStreamQuery(from));
        var cancelled = await Reader.ReadAsync(new EventStreamQuery(from, Type: nameof(OrderCancelledV1)));

        Assert.DoesNotContain(all.Entries, e => e.OrderId == 9201);   // zpráva o faktu před „od kdy“
        Assert.Contains(all.Entries, e => e.OrderId == 9203);
        Assert.Equal([9202], cancelled.Entries.Select(e => e.OrderId!.Value));
    }

    [Fact]
    public async Task Limit_NejnovejsiZpravyAPriznak()
    {
        var from = DateTime.UtcNow.AddSeconds(-1);
        for (var i = 0; i < 5; i++)
        {
            await PublishAsync(new OrderConfirmedV1(Guid.NewGuid(), DateTime.UtcNow, 9300 + i));
        }

        var result = await Reader.ReadAsync(new EventStreamQuery(from, Type: nameof(OrderConfirmedV1), Limit: 2));

        Assert.True(result.Truncated);
        Assert.Equal([9303, 9304], result.Entries.Where(e => e.OrderId >= 9300).Select(e => e.OrderId!.Value));
    }

    private async Task PublishAsync(MiniShop.Contracts.IIntegrationMessage message)
    {
        await using var channel = await rabbit.Connection.CreateChannelAsync(publisherConfirms: true);
        await channel.BasicPublishAsync(message.GetType().Name, string.Empty, mandatory: false,
            RabbitMqMessage.CreateProperties(message), RabbitMqMessage.Serialize(message));
    }
}
