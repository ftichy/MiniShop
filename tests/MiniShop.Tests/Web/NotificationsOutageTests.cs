using System.Net;

namespace MiniShop.Tests.Web;

/// <summary>
/// Mikroservisy M2C: výpadek služby Notifications nesmí shodit stránky Webu. E-maily a oznámení – upozornění, připomínka
/// košíku (účet ve Webu) jde nastavit dál; detail produktu se ukáže, „Dejte mi vědět“ řekne, že teď nejde.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class NotificationsOutageTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-vypadek-123";

    [Fact]
    public async Task SluzbaNebezi_StrankyFungujiSUpozornenim()
    {
        var email = $"vypadek-{Guid.NewGuid():N}@test.local";
        await web.CreateUserAsync(email, Password);
        var customer = await web.LoginAsync(email, Password);
        await web.EnsureCatalogProductAsync(9201, "Výpadek – myš", 199m);

        web.NotificationsAvailable = false;
        try
        {
            var preferences = await customer.GetAsync("/Account/EmailPreferences");
            var page = WebUtility.HtmlDecode(await preferences.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, preferences.StatusCode);
            Assert.Contains("data-track=\"notifications-unavailable\"", page);
            Assert.Contains("data-track=\"email-cart-reminders\"", page);   // nastavení účtu ve Webu jde dál

            var test = await customer.PostAsync("/Account/EmailPreferences?handler=PushTest", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(customer, "/Account/EmailPreferences") }));
            Assert.Equal(HttpStatusCode.Redirect, test.StatusCode);   // ne 500

            Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/Shop/9201")).StatusCode);
        }
        finally
        {
            web.NotificationsAvailable = true;
        }
    }
}
