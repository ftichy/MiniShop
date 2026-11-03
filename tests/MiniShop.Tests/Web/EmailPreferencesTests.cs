using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Notifications;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 10: souhlas s připomínkou opuštěného košíku – výchozí vypnuto, zákazník si ho zapne formulářem
/// a Notifications ho uvidí přes <see cref="ICustomerContacts"/> (adaptér nad účty ve Webu).
/// </summary>
[Collection(WebCollection.Name)]
public sealed class EmailPreferencesTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Souhlas_VychoziVypnuty_FormularHoZapneAVypne()
    {
        const string email = "pripominky-1@example.com";
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var customer = await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
        var userId = await UserIdAsync(email);

        Assert.False((await ContactAsync(userId))!.CartRemindersConsent);

        await PostAsync(customer, cartReminders: true);
        Assert.True((await ContactAsync(userId))!.CartRemindersConsent);
        Assert.Contains("checked", WebUtility.HtmlDecode(await customer.GetStringAsync("/Account/EmailPreferences")));

        await PostAsync(customer, cartReminders: false);   // nezaškrtnuté pole prohlížeč vůbec nepošle
        Assert.False((await ContactAsync(userId))!.CartRemindersConsent);
    }

    [Fact]
    public async Task Kontakt_NeznamyUcet_Null()
    {
        Assert.Null(await ContactAsync("neexistujici-ucet"));
    }

    private static async Task PostAsync(HttpClient customer, bool cartReminders)
    {
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(customer, "/Account/EmailPreferences");
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        if (cartReminders)
        {
            form["CartReminders"] = "true";
        }

        var response = await customer.PostAsync("/Account/EmailPreferences", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<CustomerContact?> ContactAsync(string customerId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerContacts>().FindAsync(customerId);
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }
}
