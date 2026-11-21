using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.LiveUpdates;

namespace MiniShop.Tests.Web;

/// <summary>
/// Vzhled krok F3a: hub živých aktualizací přes skutečné SignalR spojení na Web v paměti (long polling – TestServer
/// WebSockety přes HttpClient neumí; v produkci jen WebSocket). Odběr = stejná oprávnění jako stránky.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class LiveUpdatesHubTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Zakaznik_SvouObjednavkuOdebira_CiziANiExpediciNe()
    {
        var own = await CustomerWithOrderAsync("live-1@example.com");
        var foreign = await CustomerWithOrderAsync("live-2@example.com");
        await using var connection = await ConnectAsync("live-1@example.com", MiniShopWebFactory.UserPassword);

        await connection.InvokeAsync("Subscribe", LiveTopics.Order(own));
        await connection.InvokeAsync("Subscribe", LiveTopics.MyOrders);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Subscribe", LiveTopics.Order(foreign)));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Subscribe", LiveTopics.Fulfilment));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("Subscribe", "nesmysl"));
    }

    [Fact]
    public async Task Skladnik_ExpediciACiziObjednavku_VratkyAno_PrehledTrzebNe()
    {
        var order = await CustomerWithOrderAsync("live-3@example.com");
        const string email = "live-sklad@example.com";
        await web.CreateWithRolesAsync(email, MiniShopWebFactory.UserPassword, AccountRoles.Warehouse);
        await using var connection = await ConnectAsync(email, MiniShopWebFactory.UserPassword);

        await connection.InvokeAsync("Subscribe", LiveTopics.Fulfilment);
        await connection.InvokeAsync("Subscribe", LiveTopics.Returns);
        await connection.InvokeAsync("Subscribe", LiveTopics.AllOrders);
        await connection.InvokeAsync("Subscribe", LiveTopics.Order(order));   // sklad vidí všechny objednávky
    }

    [Fact]
    public async Task ZmenaStavu_SignalDojdeOdberateliObjednavkyIJehoSeznamu()
    {
        const string email = "live-4@example.com";
        var orderId = await CustomerWithOrderAsync(email);
        await using var connection = await ConnectAsync(email, MiniShopWebFactory.UserPassword);
        var received = new List<string>();
        var signal = new TaskCompletionSource();
        connection.On<string>(LiveUpdatesHub.ChangedMethod, topic =>
        {
            lock (received)
            {
                received.Add(topic);
                if (received.Count == 2)
                {
                    signal.TrySetResult();
                }
            }
        });
        await connection.InvokeAsync("Subscribe", LiveTopics.Order(orderId));
        await connection.InvokeAsync("Subscribe", LiveTopics.MyOrders);

        // Zpráva, kterou by rozeslal RabbitMQ (dočasná fronta kopie) – tady rovnou handleru kopie.
        var customerId = await CustomerIdAsync(email);
        await web.Services.GetRequiredService<IBroadcastMessageHandler>().HandleAsync(
            new OrderStatusChangedV1(Guid.NewGuid(), DateTime.UtcNow, orderId, customerId, "Confirmed"), CancellationToken.None);

        await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equivalent(new[] { LiveTopics.Order(orderId), LiveTopics.MyOrders }, received);
    }

    [Fact]
    public void Komu_ZmenaStavuVsem_RefundJenObjednavceAVratkam()
    {
        var changed = LiveUpdatesNotifier.Targets(new OrderStatusChangedV1(Guid.NewGuid(), DateTime.UtcNow, 42, "ucet-1", "Shipped"));
        Assert.Equal(
            [
                ("order:42", "order:42"), ("customer:ucet-1", LiveTopics.MyOrders), (LiveTopics.AllOrders, LiveTopics.AllOrders),
                (LiveTopics.Fulfilment, LiveTopics.Fulfilment), (LiveTopics.Returns, LiveTopics.Returns)
            ],
            changed);

        var anonymized = LiveUpdatesNotifier.Targets(new OrderStatusChangedV1(Guid.NewGuid(), DateTime.UtcNow, 42, null, "Shipped"));
        Assert.DoesNotContain(anonymized, t => t.Topic == LiveTopics.MyOrders);

        var refund = LiveUpdatesNotifier.Targets(new PaymentRefundedV1(Guid.NewGuid(), DateTime.UtcNow, 42, 100m));
        Assert.Equal([("order:42", "order:42"), (LiveTopics.Returns, LiveTopics.Returns)], refund);
    }

    private async Task<HubConnection> ConnectAsync(string email, string password)
    {
        var cookies = new CookieContainer();
        await web.LoginAsync(email, password, cookies);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri("https://localhost"), LiveUpdatesHub.Path), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(cookies) { InnerHandler = web.Server.CreateHandler() };
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private async Task<int> CustomerWithOrderAsync(string email)
    {
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        return await web.CreateConfirmedOrderForAsync(email, ShippingMethod.Pickup);
    }

    private async Task<string> CustomerIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<MiniShop.Infrastructure.Accounts.ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }
}
