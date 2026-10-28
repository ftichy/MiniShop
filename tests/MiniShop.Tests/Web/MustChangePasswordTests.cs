using System.Net;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 2: s příznakem „musí změnit heslo“ se uživatel nedostane nikam jinam než na změnu hesla.
/// Po změně příznak zmizí a aplikace funguje normálně.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class MustChangePasswordTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task DocasneHeslo_VynutiZmenu_PakJeVsechnoPristupne()
    {
        const string email = "docasne@test.local";
        const string temporaryPassword = "docasne-heslo-123";
        const string newPassword = "nove-vlastni-heslo";
        await web.CreateUserAsync(email, temporaryPassword, mustChangePassword: true);
        var client = await web.LoginAsync(email, temporaryPassword);

        var page = await client.GetAsync("/Orders");
        var api = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal("/Account/ChangePassword", page.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);

        var change = await client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/ChangePassword"),
            ["Input.CurrentPassword"] = temporaryPassword,
            ["Input.NewPassword"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword
        }));

        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Orders")).StatusCode);

        // Nové heslo platí i pro nové přihlášení (a staré už ne – LoginAsync by spadl na 200 s chybou).
        await web.LoginAsync(email, newPassword);
    }
}
