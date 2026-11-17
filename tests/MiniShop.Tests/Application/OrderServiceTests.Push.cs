using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Notifications;
using MiniShop.Application.Ordering;
using MiniShop.Contracts.Orders;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 23: oznámení push – druhý odběratel událostí objednávky v modulu Notifications (vedle e-mailu).
/// Push služba je <see cref="TestDoubles.RecordingPushSender"/>; zařízení se přihlašují přes <see cref="PushSubscriptionService"/>
/// jako ze stránky nastavení.
/// </summary>
public sealed partial class OrderServiceTests
{
    private static PushSubscribeRequest Device(string name) =>
        new($"https://fcm.googleapis.com/fcm/send/{name}", "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM", "tBHItJI5svbpez7KI4CCXg");

    private async Task SubscribeAsync(string customerId, string device)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PushSubscriptionService>()
            .SubscribeAsync(customerId, Device(device), "Android · Chrome");
    }

    private async Task<IReadOnlyList<PushDeviceDto>> DevicesAsync(string customerId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PushSubscriptionService>().ListAsync(customerId);
    }

    [Fact]
    public async Task Push_ObjednavkaPotvrzenaOdeslanaDorucena_NaVsechnaZarizeniZakaznika()
    {
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        await SubscribeAsync(Jan.CustomerId, "jan-pocitac");
        await SubscribeAsync(Petra.CustomerId, "petra-telefon");
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        await ShipAsync(order.Id);
        await Orders(s => s.CompleteAsync(order.Id));
        await ProcessOutboxAsync();

        var forJan = _push.To(Jan.CustomerId);
        Assert.Equal(6, forJan.Count);   // 3 události × 2 zařízení
        Assert.Equal(
            [$"order-{order.Id}-confirmed", $"order-{order.Id}-shipped", $"order-{order.Id}-delivered"],
            forJan.Select(m => m.Tag).Distinct().ToList());
        Assert.All(forJan, m => Assert.Equal($"/Orders/{order.Id}", m.Url));
        Assert.Contains(forJan, m => m.Title == $"Objednávka č. {order.Id} je na cestě");
        Assert.DoesNotContain(forJan, m => m.Body.Contains(Jan.Name) || m.Body.Contains(Jan.Email));   // bez osobních údajů
        Assert.Empty(_push.To(Petra.CustomerId));
        Assert.Empty(_push.Sent.Where(s => s.Message.Tag.EndsWith("-placed")));   // přijetí jen e-mailem (zákazník je u toho)
    }

    [Fact]
    public async Task Push_VratkaVyrizena_CastkaAOdkazNaObjednavku()
    {
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 2, PaymentMethod.Card);

        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí"));
        await ProcessOutboxAsync();
        await Returns(r => r.AcceptGoodsAsync(order.Id));
        await ProcessOutboxAsync();

        var returned = Assert.Single(_push.To(Jan.CustomerId), m => m.Tag == $"order-{order.Id}-return-0-completed");
        Assert.Contains("200,00 Kč vracíme na kartu", returned.Body);
    }

    [Fact]
    public async Task Push_ZarizeniUzNeexistuje_SeSmaze_OstatniDostanou()
    {
        await SubscribeAsync(Jan.CustomerId, "stary-telefon");
        await SubscribeAsync(Jan.CustomerId, "novy-telefon");
        _push.MarkGone(Device("stary-telefon").Endpoint);   // aplikace odinstalovaná → push služba 410
        var product = await AddProductAsync(price: 100m, stock: 5);

        await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        Assert.Single(_push.Sent, s => s.Endpoint == Device("novy-telefon").Endpoint);
        Assert.Single(await DevicesAsync(Jan.CustomerId));
    }

    /// <summary>
    /// Push služba poprvé nejde: zopakuje se jen zpráva pro push handler (vlastní záznam v inboxu) – e-mail o potvrzení
    /// odejde jednou, oznámení napodruhé.
    /// </summary>
    [Fact]
    public async Task Push_PushSluzbaPoprveSelze_OpakujeSeJenPush_EmailJednou()
    {
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        var product = await AddProductAsync(price: 100m, stock: 5);
        _push.FailNext = 1;

        var order = await Orders(s => s.PlaceOrderAsync(CreateRequest((product.Id, 1)), Jan));
        await ProcessOutboxAsync();
        Assert.Empty(_push.Sent);

        _time.Advance(OutboxProcessor.RetryDelay(1));
        await ProcessOutboxAsync();

        Assert.Single(_push.Sent, s => s.Message.Tag == $"order-{order.Id}-confirmed");
        Assert.Single(_emails.Sent, e => e.Subject.EndsWith($"Objednávka č. {order.Id} je potvrzená", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Push_DejteMiVedet_EmailIOznameni()
    {
        var productId = await SoldOutProductAsync();
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        await WatchAsync(productId);

        await Products(s => s.AddStockAsync(productId, 5));
        await ProcessOutboxAsync();

        Assert.Single(BackInStockEmails);
        var push = Assert.Single(_push.To(Jan.CustomerId));
        Assert.Equal(($"/Shop/{productId}", "Zboží je zase skladem"), (push.Url, push.Title));
    }

    [Fact]
    public async Task Push_DejteMiVedet_PushSluzbaNejde_EmailPresto()
    {
        var productId = await SoldOutProductAsync();
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        await WatchAsync(productId);
        _push.FailNext = 1;

        await Products(s => s.AddStockAsync(productId, 5));
        await ProcessOutboxAsync();

        Assert.Single(BackInStockEmails);   // oznámení je jen doplněk – jeho chyba e-mail nezastaví ani nezopakuje
        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task Push_SmazaniUctu_SmazeZarizeni()
    {
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        await SubscribeAsync(Petra.CustomerId, "petra-telefon");

        await Orders(s => s.AnonymizeCustomerAsync(Jan.CustomerId));
        await ProcessOutboxAsync();

        Assert.Empty(await DevicesAsync(Jan.CustomerId));
        Assert.Single(await DevicesAsync(Petra.CustomerId));
    }

    [Fact]
    public async Task Push_StejneZarizeniJinyUcet_PrevezmeHo_CiziNevypne()
    {
        await SubscribeAsync(Jan.CustomerId, "sdileny-tablet");
        await SubscribeAsync(Petra.CustomerId, "sdileny-tablet");   // na tabletu se přihlásila Petra

        Assert.Empty(await DevicesAsync(Jan.CustomerId));
        var device = Assert.Single(await DevicesAsync(Petra.CustomerId));

        await using (var scope = _services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PushSubscriptionService>();
            await service.UnsubscribeAsync(Jan.CustomerId, Device("sdileny-tablet").Endpoint);   // cizí zařízení
            await service.RemoveAsync(Jan.CustomerId, device.Id);
        }

        Assert.Single(await DevicesAsync(Petra.CustomerId));
    }

    [Fact]
    public async Task Push_NejvysDesetZarizeni_NejstarsiPryc()
    {
        for (var i = 0; i < PushSubscriptionService.MaxDevicesPerCustomer + 2; i++)
        {
            await SubscribeAsync(Jan.CustomerId, $"zarizeni-{i}");
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(PushSubscriptionService.MaxDevicesPerCustomer, (await DevicesAsync(Jan.CustomerId)).Count);
        Assert.Equal(PushSubscriptionService.MaxDevicesPerCustomer,
            await NotificationsDb(db => db.PushSubscriptions.CountAsync(s => s.CustomerId == Jan.CustomerId)));
        Assert.False(await NotificationsDb(db => db.PushSubscriptions.AnyAsync(s => s.Endpoint.EndsWith("/zarizeni-0"))));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]   // jen https
    [InlineData("neni-adresa")]
    public async Task Push_NeplatnaAdresa_Odmitnuta(string endpoint)
    {
        await using var scope = _services.CreateAsyncScope();
        await Assert.ThrowsAsync<DomainException>(() => scope.ServiceProvider.GetRequiredService<PushSubscriptionService>()
            .SubscribeAsync(Jan.CustomerId, Device("x") with { Endpoint = endpoint }, "Android · Chrome"));
    }

    [Fact]
    public async Task Push_VypnuteNaServeru_NicSeNeposila()
    {
        await SubscribeAsync(Jan.CustomerId, "jan-telefon");
        _push.IsEnabled = false;   // bez klíčů VAPID
        var product = await AddProductAsync(price: 100m, stock: 5);

        await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        Assert.Empty(_push.Sent);
        Assert.Single(_bus.Published.OfType<OrderConfirmedV1>());
    }
}
