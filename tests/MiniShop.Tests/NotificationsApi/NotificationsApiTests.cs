using System.Net;
using System.Net.Http.Json;
using MiniShop.Domain.Notifications;
using MiniShop.Infrastructure.Hosting;
using MiniShop.ServiceDefaults;
using MiniShop.Tests.Web;

namespace MiniShop.Tests.NotificationsApi;

/// <summary>
/// Mikroservisy M2C: služba Notifications (celé Notifications.Api v paměti). /api jen se servisním klíčem Webu,
/// health checky otevřené; API pro stránky Webu – „Dejte mi vědět“, oznámení k práci jen s povolenými typy,
/// neplatné zařízení push = 400; zprávy služby pro /Admin/Messages.
/// </summary>
/// <remarks>Ve stejné kolekci jako testy Webu – služby nastavují statický Serilog logger, nesmí běžet souběžně.</remarks>
[Collection(WebCollection.Name)]
public sealed class NotificationsApiTests : IClassFixture<NotificationsApiFactory>
{
    private readonly NotificationsApiFactory _api;

    public NotificationsApiTests(NotificationsApiFactory api) => _api = api;

    [Theory]
    [InlineData("GET", "/api/push/u1/devices")]
    [InlineData("POST", "/api/push/u1/test")]
    [InlineData("GET", "/api/staff-alerts/u1")]
    [InlineData("GET", "/api/back-in-stock/u1/1")]
    [InlineData("DELETE", "/api/back-in-stock/u1/1")]
    [InlineData("GET", "/api/messages")]
    public async Task BezKliceNeboSeSpatnym_401(string method, string url)
    {
        var anonymous = _api.CreateClient();
        var wrongKey = _api.CreateClient();
        wrongKey.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, new string('x', 64));

        foreach (var client in new[] { anonymous, wrongKey })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthCheck_BezKlice()
    {
        using var response = await _api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DejteMiVedet_PrihlasitDvakratAZrusit()
    {
        var client = _api.CreateAuthorizedClient();
        var user = $"zakaznik-{Guid.NewGuid():N}";

        Assert.False(await WatchingAsync(client, user, 7));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/back-in-stock/{user}/7", new { ProductName = "Myš" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/back-in-stock/{user}/7", new { ProductName = "Myš" })).StatusCode);   // dvojklik
        Assert.True(await WatchingAsync(client, user, 7));

        await client.DeleteAsync($"/api/back-in-stock/{user}/7");
        Assert.False(await WatchingAsync(client, user, 7));
    }

    [Fact]
    public async Task OznameniKPraci_UlozíJenPovoleneTypy()
    {
        var client = _api.CreateAuthorizedClient();
        var user = $"sklad-{Guid.NewGuid():N}";

        var saved = await client.PutAsJsonAsync($"/api/staff-alerts/{user}", new
        {
            Wanted = new[] { StaffAlertKind.NewOrder, StaffAlertKind.ReturnRequested },
            Allowed = new[] { StaffAlertKind.NewOrder }   // roli k vratkám nemá – rozhodl Web
        });

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal([StaffAlertKind.NewOrder], await client.GetFromJsonAsync<List<StaffAlertKind>>($"/api/staff-alerts/{user}"));
    }

    [Fact]
    public async Task ZarizeniPush_NeplatnaAdresa400_PlatnaVSeznamu()
    {
        var client = _api.CreateAuthorizedClient();
        var user = $"push-{Guid.NewGuid():N}";
        const string p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";

        var invalid = await client.PostAsJsonAsync($"/api/push/{user}/devices",
            new { Endpoint = "http://evil.example/x", P256dh = p256dh, Auth = "BTBZMqHH6r4Tts7J_aSIgg", DeviceLabel = "Test" });
        var valid = await client.PostAsJsonAsync($"/api/push/{user}/devices",
            new { Endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}", P256dh = p256dh, Auth = "BTBZMqHH6r4Tts7J_aSIgg", DeviceLabel = "Android · Chrome" });

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.NoContent), (invalid.StatusCode, valid.StatusCode));
        var devices = await client.GetStringAsync($"/api/push/{user}/devices");
        Assert.Contains("Android · Chrome", devices);
        Assert.DoesNotContain("fcm.googleapis.com", devices);   // endpoint (osobní údaj) se ven neposílá
    }

    [Fact]
    public async Task Zpravy_JenModulNotifications()
    {
        var status = await _api.CreateAuthorizedClient().GetFromJsonAsync<ServiceMessagesStatus>("/api/messages");

        Assert.Equal(["Notifications"], status!.Modules.Select(m => m.Module));
    }

    private static async Task<bool> WatchingAsync(HttpClient client, string user, int productId) =>
        (await client.GetFromJsonAsync<WatchStatus>($"/api/back-in-stock/{user}/{productId}"))!.Subscribed;

    private sealed record WatchStatus(bool Subscribed);
}
