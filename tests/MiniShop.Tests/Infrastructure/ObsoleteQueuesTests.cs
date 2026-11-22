using MiniShop.Infrastructure.Integration.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Proud B3: zrušené fronty proti skutečnému RabbitMQ – fronta, kterou někdo poslouchá (stará kopie při postupném
/// nasazení), zůstane; bez posluchače se smaže i se zprávami; neexistující fronta nevadí.
/// </summary>
public sealed class ObsoleteQueuesTests(RabbitMqTestServer rabbit) : IClassFixture<RabbitMqTestServer>
{
    [Fact]
    public async Task SPosluchacemZustane_BezPosluchaceSeSmaze_NeexistujiciNevadi()
    {
        var queue = $"minishop.test-obsolete-{Guid.NewGuid():N}";
        var missing = $"minishop.test-missing-{Guid.NewGuid():N}";
        await using var setup = await rabbit.Connection.CreateChannelAsync(publisherConfirms: true);
        await setup.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await setup.BasicPublishAsync(string.Empty, queue, mandatory: false, new BasicProperties(), "zahodit"u8.ToArray());

        // Stará kopie služby frontu ještě poslouchá → smazání RabbitMQ odmítne.
        await using var listener = await rabbit.Connection.CreateChannelAsync(publisherConfirms: false);
        var consumerTag = await listener.BasicConsumeAsync(queue, autoAck: false, new AsyncEventingBasicConsumer(listener));

        var whileListening = await RabbitMqObsoleteQueues.DeleteUnusedAsync(rabbit.Connection, [queue, missing], CancellationToken.None);
        Assert.Equal([queue], whileListening);

        // Stará kopie skončila → fronta zmizí i se zprávou.
        await listener.BasicCancelAsync(consumerTag);
        var afterStop = await RabbitMqObsoleteQueues.DeleteUnusedAsync(rabbit.Connection, [queue, missing], CancellationToken.None);
        Assert.Empty(afterStop);

        await using var check = await rabbit.Connection.CreateChannelAsync(publisherConfirms: false);
        await Assert.ThrowsAnyAsync<RabbitMQ.Client.Exceptions.OperationInterruptedException>(() => check.QueueDeclarePassiveAsync(queue));
    }

    [Fact]
    public void Zrusene_AuditniFrontaIJejiDeadLetter() =>
        Assert.Equal(["minishop.audit", "minishop.audit.dead"], RabbitMqObsoleteQueues.Names);
}
