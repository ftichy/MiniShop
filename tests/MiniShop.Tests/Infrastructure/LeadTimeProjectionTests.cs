using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Reporting;
using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Infrastructure;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Proud B2: trvalý čtenář proudu <see cref="LeadTimeProjectionWorker"/> proti skutečnému RabbitMQ a PostgreSQL –
/// zprávy z proudu → časy objednávek a záložka; restart pokračuje od záložky; přestavění přehraje historii znovu.
/// </summary>
public sealed class LeadTimeProjectionTests(RabbitMqTestServer rabbit) : IClassFixture<RabbitMqTestServer>, IDisposable
{
    private readonly TestDatabases _databases = new();

    public void Dispose() => _databases.Dispose();

    [Fact]
    public async Task ZpravyZProudu_CasyObjednavek_RestartPokracujeOdZalozky()
    {
        await using var services = BuildServices();
        var now = DateTime.UtcNow;
        await PublishJourneyAsync(5101, now, byCarrier: true);

        var worker = await StartWorkerAsync(services);
        var (first, offsetAfterFirstRun) = await WaitForOrderAsync(services, 5101);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(6), first.Confirmation);
        Assert.Equal(TimeSpan.FromMinutes(5), first.Delivery);
        Assert.Equal(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(6), first.Expedition);

        // Čtenář neběží – zprávy čekají v proudu. Nový čtenář pokračuje od uložené záložky, ne od začátku.
        await PublishJourneyAsync(5102, now, byCarrier: false);
        worker = await StartWorkerAsync(services);
        var (second, offsetAfterRestart) = await WaitForOrderAsync(services, 5102);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(offsetAfterRestart > offsetAfterFirstRun, $"záložka po restartu {offsetAfterRestart}, po prvním běhu {offsetAfterFirstRun}");
        Assert.Null(second.Delivery);   // bez dopravce – doručení se nepočítá
        Assert.Equal(TimeSpan.FromSeconds(6), second.Confirmation);
    }

    [Fact]
    public async Task Prestaveni_SmazeDataAPrehrajeProudZnovu()
    {
        await using var services = BuildServices();
        await PublishJourneyAsync(5201, DateTime.UtcNow, byCarrier: true);
        var worker = await StartWorkerAsync(services);
        await WaitForAsync(services, r => r.Delivery.Count >= 1);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LeadTimeReportService>().RebuildAsync();
        }

        // Čtenář pozná novou generaci, začne od začátku proudu a postaví totéž znovu – Ordering se na nic neptá.
        var rebuilt = await WaitForAsync(services, r => r.Generation == 1 && r.Delivery.Count >= 1);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, rebuilt.Generation);
        Assert.NotNull(rebuilt.Offset);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructureCore();
        services.AddReportingInfrastructure(_databases.Reporting);
        services.AddScoped<LeadTimeReportService>();
        services.AddSingleton(rabbit.Connection);
        return services.BuildServiceProvider();
    }

    private static async Task<LeadTimeProjectionWorker> StartWorkerAsync(IServiceProvider services)
    {
        var worker = ActivatorUtilities.CreateInstance<LeadTimeProjectionWorker>(services);
        await worker.StartAsync(CancellationToken.None);
        return worker;
    }

    /// <summary>Počká, až projekce zná celou cestu objednávky (doručeno); vrátí její časy a záložku v tu chvíli.</summary>
    private static async Task<(MiniShop.Domain.Reporting.OrderLeadTime LeadTime, long Offset)> WaitForOrderAsync(IServiceProvider services, int orderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var scope = services.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<ILeadTimeRepository>();
                var found = await repository.GetByOrderIdsAsync([orderId]);
                if (found.TryGetValue(orderId, out var leadTime) && leadTime.DeliveredAtUtc is not null)
                {
                    var checkpoint = await repository.GetCheckpointAsync(LeadTimeReportService.CheckpointName);
                    return (leadTime, checkpoint.Offset!.Value);
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Projekce nezpracovala objednávku {orderId}.");
            }

            await Task.Delay(250);
        }
    }

    private static async Task<LeadTimeReportDto> WaitForAsync(IServiceProvider services, Func<LeadTimeReportDto, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var scope = services.CreateAsyncScope())
            {
                var report = await scope.ServiceProvider.GetRequiredService<LeadTimeReportService>().GetAsync();
                if (condition(report))
                {
                    return report;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"Projekce nedoběhla: potvrzení {report.Confirmation.Count}, doručení {report.Delivery.Count}, " +
                                $"záložka {report.Offset}, generace {report.Generation}.");
                }
            }

            await Task.Delay(250);
        }
    }

    /// <summary>Objednávka: vznik → potvrzení za 6 s → odeslání za 10 min → doručení za dalších 5 min (s dopravcem).</summary>
    private async Task PublishJourneyAsync(int orderId, DateTime now, bool byCarrier)
    {
        var placed = now.AddMinutes(-20);
        await PublishAsync(new OrderStatusChangedV1(Guid.NewGuid(), placed, orderId, "ucet", "Pending"));
        await PublishAsync(new OrderStatusChangedV1(Guid.NewGuid(), placed.AddSeconds(6), orderId, "ucet", "Confirmed"));
        await PublishAsync(new OrderStatusChangedV1(Guid.NewGuid(), placed.AddMinutes(10), orderId, "ucet", "Shipped"));
        if (byCarrier)
        {
            await PublishAsync(new ShipmentStatusChangedV1(Guid.NewGuid(), placed.AddMinutes(11), orderId, "CS1", "PickedUp", placed.AddMinutes(11)));
        }

        await PublishAsync(new OrderCompletedV1(Guid.NewGuid(), placed.AddMinutes(15), orderId, "x@example.com"));   // projekci nezajímá
        await PublishAsync(new OrderStatusChangedV1(Guid.NewGuid(), placed.AddMinutes(15), orderId, "ucet", "Completed"));
    }

    private async Task PublishAsync(IIntegrationMessage message)
    {
        await using var channel = await rabbit.Connection.CreateChannelAsync(publisherConfirms: true);
        await channel.BasicPublishAsync(message.GetType().Name, string.Empty, mandatory: false,
            RabbitMqMessage.CreateProperties(message), RabbitMqMessage.Serialize(message));
    }
}
