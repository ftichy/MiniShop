using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Tests.InventoryApi;

namespace MiniShop.Tests.Web;

/// <summary>
/// Škálování krok 5: služby běží ve víc kopiích. Worker s časovačem (outbox, termíny ság, úklid, kontroly po startu)
/// smí běžet jen na vedoucí kopii (<see cref="LeaderOnlyHostedService{TWorker}"/>) – jinak by ve dvou kopiích dělal
/// stejnou práci dvakrát. Ve všech kopiích běží jen consumery RabbitMQ (zprávy si rozdělí fronta) a samotná volba vedoucí.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class WorkerRegistrationTests(MiniShopWebFactory web)
{
    private static readonly Type[] AllInstances = [typeof(LeaderElection), typeof(RabbitMqConsumersWorker)];

    [Fact]
    public void Web_WorkeryJenNaVedouciKopii()
    {
        _ = web.Services;   // postaví Web (registrace workerů se zachytí před jejich odebráním)

        AssertLeaderOnly(web.RegisteredWorkers, "OrderSagaTimeoutWorker", "CartReminderWorker", "CatalogSyncOnStartupWorker",
            "SalesReportOnStartupWorker", "PurchasesSyncOnStartupWorker", "OutboxWorker", "MessageRetentionWorker");
    }

    [Fact]
    public void Sklad_WorkeryJenNaVedouciKopii()
    {
        using var api = new InventoryApiFactory();
        _ = api.Services;

        AssertLeaderOnly(api.RegisteredWorkers, "OutboxWorker", "MessageRetentionWorker");
    }

    private static void AssertLeaderOnly(List<Type> registered, params string[] expectedLeaderOnly)
    {
        Assert.Equivalent(AllInstances, registered.Where(t => !t.IsGenericType).ToList(), strict: true);

        var leaderOnly = registered.Where(t => t.IsGenericType).ToList();
        Assert.All(leaderOnly, t => Assert.Equal(typeof(LeaderOnlyHostedService<>), t.GetGenericTypeDefinition()));
        Assert.Equivalent(expectedLeaderOnly, leaderOnly.Select(t => t.GetGenericArguments()[0].Name).ToList(), strict: true);
    }
}
