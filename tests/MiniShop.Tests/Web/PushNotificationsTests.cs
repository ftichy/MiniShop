using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Notifications;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Notifications;
using MiniShop.ServiceDefaults;
using MiniShop.Web.Notifications;
using AppPushMessage = MiniShop.Application.Notifications.PushMessage;
using DomainPushSubscription = MiniShop.Domain.Notifications.PushSubscription;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 23: oznámení push ve Webu – stránka nastavení (zapnutí zařízení JSONem s antiforgery tokenem, seznam,
/// zkušební oznámení), klíče VAPID, šifrování knihovnou Web Push proti podvržené push službě, soukromí v trasách.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class PushNotificationsTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-zakaznika-push-123";
    private const string Page = "/Account/EmailPreferences";

    // Veřejný klíč a tajemství prohlížeče z příkladu v RFC 8291 – platný bod na křivce P-256.
    private const string BrowserP256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string BrowserAuth = "BTBZMqHH6r4Tts7J_aSIgg";

    [Fact]
    public async Task Nastaveni_ZakaznikZapneZarizeni_VidiHoVSeznamu_ZkusebniOznameniDorazi()
    {
        var (client, userId) = await NewCustomerAsync();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("data-push-key=\"test-vapid-public-key\"", page);
        Assert.Contains("/js/push.js", page);

        var endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";
        var response = await PostJsonAsync(client, "PushSubscribe", new { endpoint, p256dh = BrowserP256dh, auth = BrowserAuth },
            userAgent: "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        page = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("Android · Chrome", page);
        Assert.DoesNotContain(endpoint, page);   // endpoint (identifikátor zařízení) na stránku nejde

        var test = await client.PostAsync($"{Page}?handler=PushTest", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, Page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, test.StatusCode);
        Assert.Equal("MiniShop – zkouška", Assert.Single(web.Push.To(userId)).Title);
        Assert.DoesNotContain(web.Logs.Entries, m => m.Contains(endpoint, StringComparison.Ordinal));   // ani do logu
    }

    [Fact]
    public async Task Nastaveni_BezAntiforgeryTokenu_NeboNeplatnaAdresa_Odmitnuto()
    {
        var (client, userId) = await NewCustomerAsync();

        var withoutToken = await client.PostAsJsonAsync($"{Page}?handler=PushSubscribe",
            new { endpoint = "https://fcm.googleapis.com/fcm/send/x", p256dh = BrowserP256dh, auth = BrowserAuth });
        var invalid = await PostJsonAsync(client, "PushSubscribe", new { endpoint = "http://evil.example/x", p256dh = BrowserP256dh, auth = BrowserAuth });

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(await DevicesAsync(userId));
    }

    [Fact]
    public async Task Nastaveni_VypnoutNaZarizeni_AOdebratZeSeznamu()
    {
        var (client, userId) = await NewCustomerAsync();
        var phone = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";
        var laptop = $"https://updates.push.services.mozilla.com/wpush/v2/{Guid.NewGuid():N}";
        await PostJsonAsync(client, "PushSubscribe", new { endpoint = phone, p256dh = BrowserP256dh, auth = BrowserAuth });
        await PostJsonAsync(client, "PushSubscribe", new { endpoint = laptop, p256dh = BrowserP256dh, auth = BrowserAuth });

        Assert.Equal(HttpStatusCode.NoContent, (await PostJsonAsync(client, "PushUnsubscribe", new { endpoint = phone })).StatusCode);
        var remaining = Assert.Single(await DevicesAsync(userId));
        await client.PostAsync($"{Page}?handler=RemoveDevice&id={remaining.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, Page)
        }));

        Assert.Empty(await DevicesAsync(userId));
    }

    /// <summary>
    /// E-shop krok 24: skladník (bez role Zákazník) na stránce vidí oznámení k práci jen pro svou roli, připomínku
    /// košíku ne; uloží se jen typy s rolí. Při odeslání rozhoduje databáze – zablokovaný účet nic nedostane.
    /// </summary>
    [Fact]
    public async Task Zamestnanec_OznameniKPraci_JenProSvouRoli_ZablokovanyNic()
    {
        var email = $"sklad-{Guid.NewGuid():N}@test.local";
        await web.CreateWithRolesAsync(email, Password, MiniShop.Infrastructure.Accounts.AccountRoles.Warehouse);
        var client = await web.LoginAsync(email, Password);
        string userId;
        await using (var scope = web.Services.CreateAsyncScope())
        {
            userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("data-track=\"staff-alert-NewOrder\"", page);
        Assert.Contains("data-track=\"staff-alert-OutOfStock\"", page);
        Assert.DoesNotContain("data-track=\"staff-alert-ReturnRequested\"", page);   // podpora, ne sklad
        Assert.DoesNotContain("data-track=\"email-cart-reminders\"", page);         // košík jen zákazník

        var saved = await client.PostAsync($"{Page}?handler=StaffAlerts", new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, Page)),
            new("kinds", "NewOrder"),
            new("kinds", "ReturnRequested")   // podvržené – role chybí, nezapne se
        ]));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        await using (var scope = web.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            Assert.Equal([MiniShop.Domain.Notifications.StaffAlertKind.NewOrder],
                (await services.GetRequiredService<StaffAlertService>().GetAsync(userId)).ToList());

            var directory = services.GetRequiredService<IStaffDirectory>();
            Assert.Equal([userId], await directory.FilterAllowedAsync(MiniShop.Domain.Notifications.StaffAlertKind.NewOrder, [userId]));
            Assert.Empty(await directory.FilterAllowedAsync(MiniShop.Domain.Notifications.StaffAlertKind.ReturnRequested, [userId]));

            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(userId))!, DateTimeOffset.MaxValue);   // zablokován správcem
            Assert.Empty(await directory.FilterAllowedAsync(MiniShop.Domain.Notifications.StaffAlertKind.NewOrder, [userId]));
        }
    }

    [Fact]
    public void KlicVapid_ZPrikazu_ProjdeKontrolou_SpatnyTvarNe()
    {
        var (publicKey, privateKey) = GenerateVapidKeysCommand.Create();

        Assert.True(PushSettings.FromValues(publicKey, privateKey, "https://minishop.test").IsConfigured);
        Assert.False(PushSettings.FromValues(null, " ", null).IsConfigured);   // bez klíčů = vypnuto, Web běží
        Assert.Throws<InvalidOperationException>(() => PushSettings.FromValues(publicKey, null, null));   // jen jeden
        Assert.Throws<InvalidOperationException>(() => PushSettings.FromValues(privateKey, publicKey, null));   // prohozené
    }

    /// <summary>Skutečná knihovna Web Push: zpráva zašifrovaná (aes128gcm) a podepsaná (VAPID); 410 = zařízení pryč.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Created, PushDeliveryResult.Sent)]
    [InlineData(HttpStatusCode.Gone, PushDeliveryResult.Gone)]
    [InlineData(HttpStatusCode.NotFound, PushDeliveryResult.Gone)]
    public async Task WebPushSender_SifrujePodepiseAPozna410(HttpStatusCode status, PushDeliveryResult expected)
    {
        var (publicKey, privateKey) = GenerateVapidKeysCommand.Create();
        var service = new FakePushService(status);
        var sender = new WebPushSender(new HttpClient(service), PushSettings.FromValues(publicKey, privateKey, "https://minishop.test"),
            NullLogger<WebPushSender>.Instance);
        var device = new DomainPushSubscription("zakaznik", "https://fcm.googleapis.com/fcm/send/abc", BrowserP256dh, BrowserAuth,
            "Android · Chrome", DateTime.UtcNow);

        var result = await sender.SendAsync(device, new AppPushMessage("Objednávka č. 1 je na cestě", "Text", "/Orders/1", "order-1-shipped"));

        Assert.Equal(expected, result);
        var request = Assert.Single(service.Requests);
        Assert.Equal("https://fcm.googleapis.com/fcm/send/abc", request.Uri);
        Assert.StartsWith("vapid t=", request.Authorization);
        Assert.Contains($"k={publicKey}", request.Authorization);
        Assert.Equal("aes128gcm", request.ContentEncoding);
        Assert.Equal(((int)WebPushSender.TimeToLive.TotalSeconds).ToString(), request.Ttl);
        Assert.False(System.Text.Encoding.UTF8.GetString(request.Body).Contains("Objednávka", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebPushSender_Chyba5xx_Vyjimka_ZpravaSeZopakuje()
    {
        var (publicKey, privateKey) = GenerateVapidKeysCommand.Create();
        var sender = new WebPushSender(new HttpClient(new FakePushService(HttpStatusCode.ServiceUnavailable)),
            PushSettings.FromValues(publicKey, privateKey, null), NullLogger<WebPushSender>.Instance);
        var device = new DomainPushSubscription("zakaznik", "https://fcm.googleapis.com/fcm/send/abc", BrowserP256dh, BrowserAuth,
            "Android · Chrome", DateTime.UtcNow);

        await Assert.ThrowsAsync<PushServiceClientException>(() => sender.SendAsync(device, new AppPushMessage("T", "B", "/", "t")));
    }

    [Fact]
    public void Trasa_AdresaPushSluzby_JenJmenoServeru()
    {
        using var activity = new Activity("POST").Start();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send/tajne-id-zarizeni");
        activity.SetTag("url.full", request.RequestUri!.ToString());

        PushServiceHosts.Redact(activity, request);
        Assert.Equal("https://fcm.googleapis.com/…", activity.GetTagItem("url.full"));

        var other = new HttpRequestMessage(HttpMethod.Get, "https://inventory-api/api/products");
        activity.SetTag("url.full", other.RequestUri!.ToString());
        PushServiceHosts.Redact(activity, other);
        Assert.Equal("https://inventory-api/api/products", activity.GetTagItem("url.full"));   // ostatní beze změny
        Assert.True(PushServiceHosts.IsPushService(new Uri("https://web.push.apple.com/abc")));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Edg/140.0", "Windows · Edge")]
    [InlineData("Mozilla/5.0 (Android 15; Mobile; rv:140.0) Gecko/140.0 Firefox/140.0", "Android · Firefox")]
    [InlineData(null, "Zařízení · prohlížeč")]
    public void PopisekZarizeni_JenSystemAProhlizec(string? userAgent, string expected) =>
        Assert.Equal(expected, PushDeviceLabel.FromUserAgent(userAgent));

    private async Task<(HttpClient Client, string UserId)> NewCustomerAsync()
    {
        var email = $"push-{Guid.NewGuid():N}@test.local";
        await web.CreateUserAsync(email, Password);
        await using var scope = web.Services.CreateAsyncScope();
        var userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
        return (await web.LoginAsync(email, Password), userId);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string handler, object body, string? userAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Page}?handler={handler}") { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, Page));
        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return await client.SendAsync(request);
    }

    private async Task<IReadOnlyList<PushDeviceDto>> DevicesAsync(string userId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PushSubscriptionService>().ListAsync(userId);
    }

    /// <summary>Podvržená push služba – zapamatuje si požadavek a odpoví zadaným stavem.</summary>
    private sealed class FakePushService(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Uri, string Authorization, string? ContentEncoding, string? Ttl, byte[] Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content?.Headers.ContentEncoding.FirstOrDefault(),
                request.Headers.TryGetValues("TTL", out var ttl) ? ttl.First() : null,
                request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(status);
        }
    }
}
