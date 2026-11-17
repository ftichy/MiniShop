using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Notifications;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Notifications;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Application;

/// <summary>
/// E-shop krok 24: oznámení k práci pro zaměstnance – sklad (nová objednávka, došlé zboží), podpora (žádost o vratku).
/// Dostane je jen ten, kdo si typ zaškrtl A k němu má (při odeslání) roli.
/// </summary>
public sealed partial class OrderServiceTests
{
    private const string Skladnik = "zamestnanec-sklad";
    private const string Podpora = "zamestnanec-podpora";

    private async Task WantStaffAlertsAsync(string userId, params StaffAlertKind[] kinds)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StaffAlertService>().SetAsync(userId, kinds, Enum.GetValues<StaffAlertKind>());
    }

    /// <summary>Skladník a podpora mají role, zařízení i zaškrtnuté „svoje“ typy.</summary>
    private async Task StaffReadyAsync()
    {
        _staff.Set(Skladnik, StaffAlertKind.NewOrder, StaffAlertKind.OutOfStock);
        _staff.Set(Podpora, StaffAlertKind.ReturnRequested);
        await SubscribeAsync(Skladnik, "sklad-telefon");
        await SubscribeAsync(Podpora, "podpora-telefon");
        await WantStaffAlertsAsync(Skladnik, StaffAlertKind.NewOrder, StaffAlertKind.OutOfStock);
        await WantStaffAlertsAsync(Podpora, StaffAlertKind.ReturnRequested);
    }

    [Fact]
    public async Task StaffPush_NovaObjednavka_DostaneSklad_PodporaNe()
    {
        await StaffReadyAsync();
        var product = await AddProductAsync(price: 100m, stock: 5);

        var order = await PlaceConfirmedOrderAsync(product.Id, quantity: 1);

        var alert = Assert.Single(_push.To(Skladnik));
        Assert.Equal(($"Nová objednávka č. {order.Id} k zabalení", "/Admin/Fulfilment"), (alert.Title, alert.Url));
        Assert.DoesNotContain(Jan.Name, alert.Body);   // bez osobních údajů
        Assert.Empty(_push.To(Podpora));
    }

    [Fact]
    public async Task StaffPush_ZadostOVratku_DostanePodpora()
    {
        await StaffReadyAsync();
        var product = await AddProductAsync(price: 100m, stock: 5);
        var order = await DeliveredOrderAsync(product.Id, quantity: 2, PaymentMethod.CashOnDelivery);

        await Orders(s => s.RequestReturnAsync(order.Id, OrderAccess.Customer(Jan.CustomerId), "Nesedí"));
        await ProcessOutboxAsync();

        var alert = Assert.Single(_push.To(Podpora));
        Assert.Equal(($"Žádost o vratku k objednávce č. {order.Id}", "/Admin/Returns"), (alert.Title, alert.Url));
    }

    [Fact]
    public async Task StaffPush_DosloZbozi_SNazvemProduktu_NaskladneniNic()
    {
        await StaffReadyAsync();
        var productId = await SoldOutProductAsync();   // Petra vykoupí sklad → Vyprodáno

        var alert = Assert.Single(_push.To(Skladnik), m => m.Title == "Došlo zboží");
        Assert.Equal(("Testovací produkt je vyprodaný.", "/Products"), (alert.Body, alert.Url));

        var before = _push.To(Skladnik).Count;
        await Products(s => s.AddStockAsync(productId, 5));
        await ProcessOutboxAsync();
        Assert.Equal(before, _push.To(Skladnik).Count);   // naskladnění zaměstnancům nic neposílá
    }

    [Fact]
    public async Task StaffPush_BezZaskrtnutiNeboOdebranaRole_Nic()
    {
        _staff.Set(Skladnik, StaffAlertKind.NewOrder);
        await SubscribeAsync(Skladnik, "sklad-telefon");   // zařízení má, ale typ nezaškrtl
        var product = await AddProductAsync(price: 100m, stock: 5);

        await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        Assert.Empty(_push.To(Skladnik));

        await WantStaffAlertsAsync(Skladnik, StaffAlertKind.NewOrder);
        _staff.Set(Skladnik);   // roli mezitím odebrali – zaškrtnutí zůstalo, rozhoduje role při odeslání
        await PlaceConfirmedOrderAsync(product.Id, quantity: 1);
        Assert.Empty(_push.To(Skladnik));
    }

    [Fact]
    public async Task StaffAlerts_ZapnoutJenTypySRoli_SmazaniUctuZapomene()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<StaffAlertService>();
            // Formulář pošle i typ, ke kterému role není – nezapne se.
            await service.SetAsync(Skladnik, [StaffAlertKind.NewOrder, StaffAlertKind.ReturnRequested], [StaffAlertKind.NewOrder]);
            Assert.Equal([StaffAlertKind.NewOrder], (await service.GetAsync(Skladnik)).ToList());
        }

        await Orders(s => s.AnonymizeCustomerAsync(Skladnik));
        await ProcessOutboxAsync();

        Assert.False(await NotificationsDb(db => db.StaffAlertSubscriptions.AnyAsync(s => s.UserId == Skladnik)));
    }
}
