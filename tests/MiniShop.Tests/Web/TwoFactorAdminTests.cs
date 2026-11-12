using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;
using MiniShop.Web.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// 2FA krok A2: admin musí mít dvoufázové ověření. Bez něj se smí jen přihlásit a nastavit si ho – administrace
/// (stránky, akce i API) ho posílá na /Account/TwoFactor. Platí i pro Google, pro povýšeného uživatele a po
/// nouzovém <c>reset-2fa</c>. Vývojový AdminBootstrap = admin bez 2FA, který si ho při prvním přihlášení nastaví sám.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class TwoFactorAdminTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-admin-2fa";

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Fulfilment")]
    [InlineData("/Products")]
    [InlineData("/Orders/Journey/1")]
    public async Task AdminBez2fa_StrankyAdministrace_PosleNaNastaveni(string url)
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var client = await web.LoginAsync(email, Password);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/TwoFactor", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/Admin/Fulfilment?handler=StartPacking")]
    [InlineData("/Admin/Users/Detail/neexistuje?handler=Promote")]
    [InlineData("/Products?handler=Delete&id=1")]
    [InlineData("/Orders?handler=SyncCatalog")]
    public async Task AdminBez2fa_AkceAdministrace_PosleNaNastaveni(string url)
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var client = await web.LoginAsync(email, Password);
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/TwoFactor");

        var response = await client.PostAsync(url, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/TwoFactor", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("POST", "/api/orders/999/complete")]
    [InlineData("GET", "/api/journeys/orders/1")]
    public async Task AdminBez2fa_Api_Vraci403(string method, string url)
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var client = await web.LoginAsync(email, Password);

        var response = method == "GET" ? await client.GetAsync(url) : await client.PostAsJsonAsync(url, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminBez2fa_ViCiziObjednavky_JenSvojeStejneJakoZakaznik()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var orderId = await web.CreateOrderForAsync(MiniShopWebFactory.UserEmail);
        var client = await web.LoginAsync(email, Password);

        // Detail cizí objednávky: admin s 2FA ji vidí, bez 2FA ne (jako cizí zákazník = 404).
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Orders/{orderId}")).StatusCode);
        var adminWith2fa = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await adminWith2fa.GetAsync($"/Orders/{orderId}")).StatusCode);
    }

    [Fact]
    public async Task AdminBez2fa_NastaviNaStrance_ASpravaSeOdemkneBezNovehoPrihlaseni()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var client = await web.LoginAsync(email, Password);

        var page = await client.GetStringAsync("/Account/TwoFactor");
        Assert.Contains("two-factor-required", page);   // vysvětlení, proč tu je
        Assert.DoesNotContain("handler=Disable", page);

        var key = (await web.GetAuthenticatorKeyAsync(email));
        var enable = await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor",
            new() { ["Code"] = TestTotp.Compute(key) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Users")).StatusCode);
    }

    [Fact]
    public async Task AdminS2fa_PoHeslePotrebujeKod_ZarizeniSiNepamatuje()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: true);
        var browser = web.CreateBrowser();

        var login = await PostAsync(browser, "/Account/Login", "/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = Password });

        Assert.Contains("/Account/LoginWith2fa", login.Headers.Location!.OriginalString);
        Assert.Contains("/Account/Login", (await browser.GetAsync("/Admin/Users")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PovyseniUzivatele_ZapamatovaneZarizeniPrestaneKodPreskakovat()
    {
        var email = NewEmail();
        await web.CreateUserAsync(email, Password);
        var key = await EnableTwoFactorViaPagesAsync(email);

        // Zákazník si zařízení zapamatoval: příští přihlášení kód nechce.
        var browser = web.CreateBrowser();
        await PostAsync(browser, "/Account/Login", "/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = Password });
        await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa",
            new() { ["Input.Code"] = TestTotp.Compute(key), ["Input.RememberMachine"] = "true" });
        var again = await PostAsync(browser, "/Account/Login", "/", new() { ["Input.Email"] = email, ["Input.Password"] = Password });
        Assert.DoesNotContain("LoginWith2fa", again.Headers.Location!.OriginalString);

        // Povýšení na admina (stejný prohlížeč s cookie zařízení) – kód se musí zadávat.
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.RemoveFromRoleAsync(user, AccountRoles.User);
            await users.AddToRoleAsync(user, AccountRoles.Admin);
            await users.UpdateSecurityStampAsync(user);
        }

        var afterPromotion = await PostAsync(browser, "/Account/Login", "/", new() { ["Input.Email"] = email, ["Input.Password"] = Password });
        Assert.Contains("/Account/LoginWith2fa", afterPromotion.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Google_AdminBez2fa_PrihlasiSeAleAdministraceJeZamcena()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: false);
        var browser = web.CreateBrowser();

        var callback = await GoogleLoginAsync(browser, email);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var denied = await browser.GetAsync("/Admin/Users");
        Assert.Contains("/Account/TwoFactor", denied.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Account/TwoFactor")).StatusCode);
    }

    [Fact]
    public async Task Google_AdminS2fa_PoGoogluJesteKod()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: true);
        var browser = web.CreateBrowser();

        var callback = await GoogleLoginAsync(browser, email);

        Assert.Contains("/Account/LoginWith2fa", callback.Headers.Location!.OriginalString);
        // Google samotný nestačí – bez kódu administrace nepustí.
        Assert.Contains("/Account/Login", (await browser.GetAsync("/Admin/Users")).Headers.Location!.OriginalString);

        var code = await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa",
            new() { ["Input.Code"] = TestTotp.Compute(await web.GetAuthenticatorKeyAsync(email)) });
        Assert.Equal(HttpStatusCode.Redirect, code.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Admin/Users")).StatusCode);
    }

    [Fact]
    public async Task Reset2fa_ZrusiOverení_ZapiseAuditAOdhlasiStarePrihlaseni()
    {
        var email = NewEmail();
        await web.CreateAdminAsync(email, Password, twoFactor: true);
        var oldKey = await web.GetAuthenticatorKeyAsync(email);
        var session = await web.LoginAsync(email, Password);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/Admin/Users")).StatusCode);

        await using (var scope = web.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<TwoFactorReset>().ResetAsync(email);
            Assert.True(result.Succeeded, result.Message);
        }

        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.False(user.TwoFactorEnabled);
            Assert.Equal(0, await users.CountRecoveryCodesAsync(user));
            Assert.NotEqual(oldKey, await users.GetAuthenticatorKeyAsync(user));

            var audit = await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().AuditEntries
                .SingleAsync(entry => entry.TargetUserId == user.Id);
            Assert.Equal(AccountAuditAction.TwoFactorReset, audit.Action);
            Assert.Equal(AccountAuditEntry.ServerCommandActor, audit.ActorEmail);
        }

        // Po resetu se admin přihlásí jen heslem, ale administrace zůstane zamčená, dokud 2FA nenastaví znovu.
        var again = await web.LoginAsync(email, Password);
        Assert.Contains("/Account/TwoFactor", (await again.GetAsync("/Admin/Users")).Headers.Location!.OriginalString);
        Assert.DoesNotContain(email, string.Join('\n', web.Logs.Entries), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reset2fa_NeznamyUcet_Selze()
    {
        await using var scope = web.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<TwoFactorReset>().ResetAsync("nikdo@test.local");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void PrikazReset2fa_RozpoznaArgumenty()
    {
        Assert.True(ResetTwoFactorCommand.IsRequested(["reset-2fa", "--email", "a@b.cz"]));
        Assert.False(ResetTwoFactorCommand.IsRequested(["create-admin"]));
    }

    // ---- pomocné ----

    private static string NewEmail() => $"a2-{Guid.NewGuid():N}@test.local";

    private async Task<string> EnableTwoFactorViaPagesAsync(string email)
    {
        var client = await web.LoginAsync(email, Password);
        await client.GetStringAsync("/Account/TwoFactor");   // vytvoří klíč
        var key = await web.GetAuthenticatorKeyAsync(email);
        await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor", new() { ["Code"] = TestTotp.Compute(key) });
        return key;
    }

    /// <summary>„Přihlásit přes Google“ jako prohlížeč; vrací odpověď návratu na ExternalLogin?handler=Callback.</summary>
    private async Task<HttpResponseMessage> GoogleLoginAsync(HttpClient client, string email)
    {
        var headers = new Dictionary<string, string>
        {
            [FakeGoogle.IdHeader] = Guid.NewGuid().ToString("N"),
            [FakeGoogle.EmailHeader] = email,
            [FakeGoogle.EmailVerifiedHeader] = "true",
            [FakeGoogle.NameHeader] = "Admin Google"
        };
        var challenge = await PostAsync(client, "/Account/ExternalLogin?returnUrl=%2F", "/Account/Login",
            new() { ["provider"] = GoogleLogin.Scheme }, headers);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        return await client.GetAsync(challenge.Headers.Location);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string tokenPage,
        Dictionary<string, string> fields, Dictionary<string, string>? headers = null)
    {
        fields["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, tokenPage);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request);
    }
}
