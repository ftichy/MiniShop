using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Contracts.Orders;
using MiniShop.Infrastructure.Integration;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;
using RabbitMQ.Client;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 8: části RabbitMQ, které jdou ověřit bez běžícího brokeru.
/// Cestu přes skutečný RabbitMQ si vyzkoušíte ručně (docs/kroky/krok-08-rabbitmq.md).
/// </summary>
public sealed class RabbitMqTests
{
    private static readonly OrderPlacedV1 Placed = new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        42, "jan@example.com", 300m, [new OrderLineV1(7, 3)]);

    [Fact]
    public void Zprava_NeseIdTypAJeTrvala()
    {
        var properties = RabbitMqMessage.CreateProperties(Placed);

        Assert.Equal("11111111-2222-3333-4444-555555555555", properties.MessageId);   // pro inbox příjemce
        Assert.Equal("OrderPlacedV1", properties.Type);                               // podle něj příjemce načte JSON
        Assert.Equal("application/json", properties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, properties.DeliveryMode);              // přežije restart brokeru
    }

    [Fact]
    public void Zprava_TeloJdeNacistZpetStejnouCestouJakoConsumer()
    {
        var properties = RabbitMqMessage.CreateProperties(Placed);
        var body = RabbitMqMessage.Serialize(Placed);

        var read = IntegrationEventReader.Read(properties.Type!, Encoding.UTF8.GetString(body));

        var placed = Assert.IsType<OrderPlacedV1>(read);
        Assert.Equal(Placed.OrderId, placed.OrderId);
        Assert.Equal(Placed.Lines, placed.Lines);
    }

    /// <summary>Na portu 1 nic neposlouchá – bus musí ohlásit "broker nedostupný", ne obecnou chybu.</summary>
    [Fact]
    public async Task Bus_NedostupnyBroker_VyhodiIntegrationBusUnavailable()
    {
        var options = new RabbitMqOptions { HostName = "localhost", Port = 1 };
        await using var connection = new RabbitMqConnection(options, NullLogger<RabbitMqConnection>.Instance);
        await using var bus = new RabbitMqIntegrationEventBus(connection, NullLogger<RabbitMqIntegrationEventBus>.Instance);

        await Assert.ThrowsAsync<IntegrationBusUnavailableException>(() => bus.PublishAsync(Placed));
    }
}
