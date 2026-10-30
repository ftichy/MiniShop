using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Ordering;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 9: otevření webu – objednávat jde jen s potvrzeným e-mailem, uživatel si může smazat účet
/// (objednávky zůstanou bez jména a e-mailu, z auditu zmizí e-mail).
/// </summary>
[Collection(WebCollection.Name)]
public sealed class AccountDeletionTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-smazani-123";
    private readonly string _email = $"smazat-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task NepotvrzenyEmail_ObjednatNejde_StrankaVyzveKPotvrzeni()
    {
        await web.CreateUserAsync(_email, Password, emailConfirmed: false);
        var client = await web.LoginAsync(_email, Password);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Orders"));
        var api = await client.PostAsJsonAsync("/api/orders", new { items = new[] { new { productId = 1, quantity = 1 } } });

        Assert.DoesNotContain("id=\"order-form\"", page);
        Assert.Contains("Objednávat můžete, až potvrdíte svůj e-mail", page);
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
    }

    [Fact]
    public async Task SmazaniUctu_ObjednavkyBezJmenaAEmailu_AuditBezEmailu_PrihlaseniNejde()
    {
        await web.CreateUserAsync(_email, Password);
        var orderId = await web.CreateOrderForAsync(_email, cancelled: true);
        var userId = await UserIdAsync();
        // Záznam v auditu: admin účet zablokuje a odblokuje.
        await Administration(a => a.LockAsync(AdminId(), userId));
        await Administration(a => a.UnlockAsync(AdminId(), userId));
        var client = await web.LoginAsync(_email, Password);

        var response = await DeleteAsync(client, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Účet je smazaný", WebUtility.HtmlDecode(await client.GetStringAsync("/")));
        Assert.Null(await FindUserAsync());

        var order = await OrderAsync(orderId);
        Assert.Equal(Order.AnonymizedCustomerName, order.CustomerName);
        Assert.Equal(string.Empty, order.CustomerEmail);
        Assert.Equal(userId, order.CustomerId);   // vazba na neexistující účet – sama o sobě nikoho neidentifikuje

        var audit = await Administration(a => a.GetAuditAsync(userId, 10));
        Assert.Equal(2, audit.Count);
        Assert.All(audit, entry => Assert.Equal(AccountAuditEntry.DeletedAccountEmail, entry.TargetEmail));

        var browser = web.CreateBrowser();
        var login = await browser.PostAsync("/Account/Login", await LoginFormAsync(browser));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);   // nepřihlášen
    }

    [Fact]
    public async Task NevyrizenaObjednavka_UcetNejdeSmazat_NicSeNezmeni()
    {
        await web.CreateUserAsync(_email, Password);
        var orderId = await web.CreateOrderForAsync(_email);   // Pending
        var client = await web.LoginAsync(_email, Password);

        var response = await DeleteAsync(client, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("nevyřízené objednávky", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.NotNull(await FindUserAsync());
        Assert.Equal(_email, (await OrderAsync(orderId)).CustomerEmail);
    }

    [Fact]
    public async Task SpatneHeslo_UcetZustane()
    {
        await web.CreateUserAsync(_email, Password);
        var client = await web.LoginAsync(_email, Password);

        var response = await DeleteAsync(client, "uplne-jine-heslo");

        Assert.Contains("Heslo nesouhlasí", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.NotNull(await FindUserAsync());
    }

    [Fact]
    public async Task Admin_SvujUcetSmazatNemuze()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Account/Delete"));
        var response = await DeleteAsync(admin, MiniShopWebFactory.AdminPassword);

        Assert.Contains("Admin si účet smazat nemůže", page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await FindUserAsync(MiniShopWebFactory.AdminEmail));
    }

    [Fact]
    public async Task Soukromi_VerejnaStranka_OdkazVPaticce()
    {
        var anonymous = web.CreateBrowser();

        var privacy = WebUtility.HtmlDecode(await anonymous.GetStringAsync("/Privacy"));
        var home = await anonymous.GetStringAsync("/");

        Assert.Contains("Co ukládáme", privacy);
        Assert.Contains("href=\"/Privacy\"", home);
    }

    private async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string password) =>
        await client.PostAsync("/Account/Delete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Delete"),
            ["Password"] = password
        }));

    private async Task<FormUrlEncodedContent> LoginFormAsync(HttpClient client) =>
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Login"),
            ["Input.Email"] = _email,
            ["Input.Password"] = Password
        });

    private async Task<ApplicationUser?> FindUserAsync(string? email = null)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email ?? _email);
    }

    private async Task<string> UserIdAsync() => (await FindUserAsync())!.Id;

    private string AdminId() => FindUserAsync(MiniShopWebFactory.AdminEmail).GetAwaiter().GetResult()!.Id;

    private async Task<Order> OrderAsync(int id)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    private async Task<T> Administration<T>(Func<AccountAdministration, Task<T>> action)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountAdministration>());
    }
}
