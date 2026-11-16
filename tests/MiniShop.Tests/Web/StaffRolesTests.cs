using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 12A: víc rolí najednou – zaměstnanec, který je i zákazník; přidělení rolí formulářem (jen správce uživatelů)
/// s e-mailem dotyčnému. Dělení správy po oblastech je krok 12B – zatím každá zaměstnanecká role smí celou správu.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class StaffRolesTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-zamestnance-123";

    [Fact]
    public async Task Skladnik_SmiDoSpravy_ObjednavatNe()
    {
        var email = NewEmail();
        await web.CreateWithRolesAsync(email, Password, AccountRoles.Warehouse);
        var client = await web.LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Fulfilment")).StatusCode);
        var checkout = await client.GetAsync("/Checkout");
        Assert.Equal(HttpStatusCode.Redirect, checkout.StatusCode);
        Assert.Contains("/Account/AccessDenied", checkout.Headers.Location!.OriginalString);
        var orders = WebUtility.HtmlDecode(await client.GetStringAsync("/Orders"));
        Assert.Contains("Všechny objednávky", orders);
        Assert.DoesNotContain("data-track=\"orders-scope\"", orders);   // přepínač jen pro zaměstnance-zákazníka
    }

    [Fact]
    public async Task ZamestnanecZakaznik_Objednava_PrepinaMojeAVsechny_CiziObjednavkuNevraci()
    {
        var email = NewEmail();
        await web.CreateWithRolesAsync(email, Password, AccountRoles.CustomerSupport, AccountRoles.User);
        var own = await web.CreateOrderForAsync(email);
        var foreign = await web.CreateOrderForAsync(MiniShopWebFactory.UserEmail);
        var client = await web.LoginAsync(email, Password);

        // Pokladna s prázdným košíkem přesměruje do košíku – podstatné je, že ne na „Přístup odepřen“.
        var checkout = await client.GetAsync("/Checkout");
        Assert.DoesNotContain("AccessDenied", checkout.Headers.Location?.OriginalString ?? string.Empty);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Cart")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Fulfilment")).StatusCode);

        var mine = WebUtility.HtmlDecode(await client.GetStringAsync("/Orders"));
        Assert.Contains("data-track=\"orders-scope\"", mine);
        Assert.Contains($"Objednávka č. {own}", mine);
        Assert.DoesNotContain($"Objednávka č. {foreign}", mine);
        var all = WebUtility.HtmlDecode(await client.GetStringAsync($"/Orders?all=true&order={foreign}"));
        Assert.Contains($"Objednávka č. {foreign}", all);

        // Cizí objednávku vidí jako podpora, ale vrátit ji za zákazníka nemůže (akce zákazníka = jen vlastní, 404).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Orders/{foreign}")).StatusCode);
        var returnForeign = await client.PostAsync($"/Orders/{foreign}?handler=Return", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/"),
            ["reason"] = "cizí"
        }));
        Assert.Equal(HttpStatusCode.NotFound, returnForeign.StatusCode);
    }

    [Fact]
    public async Task SpravceUzivatelu_PrideliRoleFormularem_UzivatelDostaneEmail()
    {
        var email = NewEmail();
        await web.CreateUserAsync(email, Password);
        var userId = await UserIdAsync(email);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Admin/Users/Detail/{userId}"));
        Assert.Contains("data-track=\"user-roles-form\"", page);

        var response = await admin.PostAsync($"/Admin/Users/Detail/{userId}?handler=Roles", new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", await MiniShopWebFactory.GetAntiforgeryTokenAsync(admin, $"/Admin/Users/Detail/{userId}")),
            new("roles", AccountRoles.User),
            new("roles", AccountRoles.Accounting)
        ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([AccountRoles.User, AccountRoles.Accounting], await RolesAsync(email));
        var mail = Assert.Single(web.Emails.Sent, m => m.To == email && m.Subject.Contains("změna oprávnění"));
        Assert.Contains("Přidáno: Účetní", mail.TextBody);
    }

    [Fact]
    public async Task ZamestnanecBezSpravyUzivatelu_FormularRoliNevidi_ZmenaNeprojde()
    {
        var email = NewEmail();
        await web.CreateWithRolesAsync(email, Password, AccountRoles.Marketing);
        var target = NewEmail();
        await web.CreateUserAsync(target, Password);
        var targetId = await UserIdAsync(target);
        var client = await web.LoginAsync(email, Password);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/Admin/Users/Detail/{targetId}"));
        Assert.DoesNotContain("data-track=\"user-roles-form\"", page);

        await client.PostAsync($"/Admin/Users/Detail/{targetId}?handler=Roles", new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, $"/Admin/Users/Detail/{targetId}")),
            new("roles", AccountRoles.UserAdmin)
        ]));

        Assert.Equal([AccountRoles.User], await RolesAsync(target));
    }

    private static string NewEmail() => $"role-{Guid.NewGuid():N}@test.local";

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<IReadOnlyList<string>> RolesAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = await users.GetRolesAsync((await users.FindByEmailAsync(email))!);
        return AccountRoles.All.Where(roles.Contains).ToList();
    }
}
