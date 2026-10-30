using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 6: správa uživatelů přes skutečné HTTP – přehled s objednávkami, povýšení na admina, zablokování.
/// Každý test si zakládá vlastní uživatele (Web je sdílený pro celou kolekci).
/// </summary>
[Collection(WebCollection.Name)]
public sealed class UserAdministrationTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-test-123";
    private readonly string _email = $"spravovany-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Prehled_UkazujeUzivateleAJehoObjednavky()
    {
        await web.CreateUserAsync(_email, Password);
        var orderId = await web.CreateOrderForAsync(_email);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var list = await PageAsync(admin, "/Admin/Users");
        var detail = await PageAsync(admin, $"/Admin/Users/Detail/{await UserIdAsync()}");

        Assert.Contains(_email, list);
        Assert.Contains(_email, detail);
        Assert.Contains($"<td>{orderId}</td>", detail);
    }

    [Fact]
    public async Task Povyseni_PoNovemPrihlaseni_MaUzivatelPristupKProduktum()
    {
        await web.CreateUserAsync(_email, Password);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var detail = await PostActionAsync(admin, "Promote");

        Assert.Contains($"{_email} je teď admin.", detail);
        Assert.Contains("povýšen na admina", detail);   // historie změn
        var promoted = await web.LoginAsync(_email, Password);
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/Products")).StatusCode);
    }

    [Fact]
    public async Task ZablokovanyUzivatel_SeNeprihlasi()
    {
        await web.CreateUserAsync(_email, Password);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var detail = await PostActionAsync(admin, "Lock");

        Assert.Contains($"Účet {_email} je zablokovaný.", detail);
        var client = web.CreateBrowser();
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Login"),
            ["Input.Email"] = _email,
            ["Input.Password"] = Password
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // žádné přesměrování = nepřihlášen
        Assert.Contains("Neplatný e-mail nebo heslo.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Admin_NaSvemDetailuNemaTlacitka()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var adminId = await UserIdAsync(MiniShopWebFactory.AdminEmail);

        var detail = await PageAsync(admin, $"/Admin/Users/Detail/{adminId}");

        Assert.Contains("To jste vy", detail);
        Assert.DoesNotContain("handler=Lock", detail);
    }

    /// <summary>Odeslání tlačítka na detailu uživatele (Post/Redirect/Get) – vrátí stránku po přesměrování.</summary>
    private async Task<string> PostActionAsync(HttpClient admin, string handler)
    {
        var detailUrl = $"/Admin/Users/Detail/{await UserIdAsync()}";
        var response = await admin.PostAsync($"{detailUrl}?handler={handler}", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(admin, detailUrl)
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await PageAsync(admin, response.Headers.Location!.OriginalString);
    }

    private async Task<string> UserIdAsync(string? email = null)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email ?? _email))!.Id;
    }

    private static async Task<string> PageAsync(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));
}
