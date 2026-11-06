using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;
using MiniShop.Web.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 10: přihlášení přes Google celou cestou přes stránky (Google nahrazuje <see cref="FakeGoogleHandler"/>):
/// nový účet, propojení podle potvrzeného e-mailu, odmítnutí nepotvrzeného, blokace, nastavení hesla, propojení
/// z nastavení účtu a smazání účtu bez hesla.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class GoogleLoginTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-google-test";
    private readonly string _email = $"google-{Guid.NewGuid():N}@test.local";
    private readonly string _googleId = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task PrihlaseniARegistrace_MajiTlacitkoGoogle()
    {
        var client = web.CreateBrowser();

        Assert.Contains("Přihlásit přes Google", await client.GetStringAsync("/Account/Login"));
        Assert.Contains("Přihlásit přes Google", await client.GetStringAsync("/Account/Register"));
    }

    [Fact]
    public async Task NovyUzivatel_ZalozeniUctuBezHesla_RovnouMuzeNakupovat()
    {
        var client = web.CreateBrowser();

        var response = await GoogleLoginAsync(client, returnUrl: "/Cart");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cart", response.Headers.Location?.OriginalString);
        // Košík = RequireCustomer: role User + potvrzený e-mail (ověřil ho Google).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Cart")).StatusCode);

        var user = await UserAsync();
        Assert.True(user.EmailConfirmed);
        Assert.Equal("Eva Nováková", user.DisplayName);
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False(await users.HasPasswordAsync(user));
        Assert.True(await users.IsInRoleAsync(user, AccountRoles.User));
        Assert.Equal(_googleId, Assert.Single(await users.GetLoginsAsync(user)).ProviderKey);
    }

    [Fact]
    public async Task DruhePrihlaseni_StejnyUcet()
    {
        await GoogleLoginAsync(web.CreateBrowser());
        var firstId = (await UserAsync()).Id;

        var response = await GoogleLoginAsync(web.CreateBrowser());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(firstId, (await UserAsync()).Id);
    }

    [Fact]
    public async Task ExistujiciPotvrzenyUcet_PropojiSe_HesloPlatiDal()
    {
        await web.CreateUserAsync(_email, Password);
        var userId = (await UserAsync()).Id;
        var client = web.CreateBrowser();

        var response = await GoogleLoginAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Orders")).StatusCode);
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(userId, (await users.FindByLoginAsync(GoogleLogin.Scheme, _googleId))?.Id);
        await web.LoginAsync(_email, Password);   // heslo funguje dál
    }

    [Fact]
    public async Task ExistujiciNepotvrzenyUcet_NepropojiSe()
    {
        // Pre-account hijacking: účet s cizím e-mailem a heslem útočníka se s Googlem majitele spojit nesmí.
        await web.CreateUserAsync(_email, Password, emailConfirmed: false);
        var client = web.CreateBrowser();

        var response = await GoogleLoginAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("e-mail zatím není potvrzený", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Orders")).StatusCode);   // nepřihlášený
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetLoginsAsync(await UserAsync()));
    }

    [Fact]
    public async Task GoogleNeoveriEmail_UcetNevznikne()
    {
        var response = await GoogleLoginAsync(web.CreateBrowser(), emailVerified: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Google nepotvrdil", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Null(await FindUserAsync());
    }

    [Fact]
    public async Task ZruseniNaGooglu_Hlaska()
    {
        var response = await GoogleLoginAsync(web.CreateBrowser(), googleId: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("nepovedlo nebo bylo zrušeno", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task ZablokovanyUcet_PresGoogleSeNeprihlasi()
    {
        await GoogleLoginAsync(web.CreateBrowser());
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(MiniShopWebFactory.AdminEmail);
            var result = await scope.ServiceProvider.GetRequiredService<AccountAdministration>()
                .LockAsync(admin!.Id, (await UserAsync()).Id);
            Assert.True(result.Succeeded, result.Message);
        }

        var client = web.CreateBrowser();
        var response = await GoogleLoginAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("zamčený nebo zablokovaný", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Orders")).StatusCode);
    }

    [Fact]
    public async Task UcetBezHesla_NastaviHeslo_PakJdePrihlasitHeslem_IOdpojitGoogle()
    {
        var client = web.CreateBrowser();
        await GoogleLoginAsync(client);

        // Změna hesla bez hesla nedává smysl – stránka pošle na nastavení.
        var change = await client.GetAsync("/Account/ChangePassword");
        Assert.Equal("/Account/SetPassword", change.Headers.Location?.OriginalString);

        // Bez hesla odpojit nejde.
        var refused = await PostAsync(client, "/Account/ExternalLogins?handler=Unlink", "/Account/ExternalLogins",
            new() { ["provider"] = GoogleLogin.Scheme, ["providerKey"] = _googleId });
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Contains("Nejdřív si nastavte heslo", WebUtility.HtmlDecode(await client.GetStringAsync("/Account/ExternalLogins")));

        var set = await PostAsync(client, "/Account/SetPassword", "/Account/SetPassword",
            new() { ["Input.NewPassword"] = Password, ["Input.ConfirmPassword"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        await web.LoginAsync(_email, Password);

        var unlink = await PostAsync(client, "/Account/ExternalLogins?handler=Unlink", "/Account/ExternalLogins",
            new() { ["provider"] = GoogleLogin.Scheme, ["providerKey"] = _googleId });
        Assert.Equal(HttpStatusCode.Redirect, unlink.StatusCode);
        Assert.Contains("Propojení je zrušené", WebUtility.HtmlDecode(await client.GetStringAsync("/Account/ExternalLogins")));
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetLoginsAsync(await UserAsync()));
    }

    [Fact]
    public async Task PropojeniZNastaveniUctu_IKNepotvrzenemuUctu()
    {
        // Přihlášený heslem = jeho účet; propojit si ho smí (automaticky podle e-mailu by to nešlo).
        await web.CreateUserAsync(_email, Password, emailConfirmed: false);
        var client = await web.LoginAsync(_email, Password);

        var link = await PostAsync(client, "/Account/ExternalLogins?handler=Link", "/Account/ExternalLogins",
            new() { ["provider"] = GoogleLogin.Scheme }, GoogleHeaders(_googleId, $"jiny-{_email}"));
        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);
        var callback = await client.GetAsync(link.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);

        Assert.Contains("Účet je propojený s Google", WebUtility.HtmlDecode(await client.GetStringAsync("/Account/ExternalLogins")));
        var googleClient = web.CreateBrowser();
        Assert.Equal(HttpStatusCode.Redirect, (await GoogleLoginAsync(googleClient)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await googleClient.GetAsync("/Orders")).StatusCode);
    }

    [Fact]
    public async Task SmazaniUctuBezHesla_PotvrzeniNovymPrihlasenimUGooglu()
    {
        var client = web.CreateBrowser();
        await GoogleLoginAsync(client);
        Assert.Contains("Ověřit přes Google", WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Delete")));

        var challenge = await PostAsync(client, "/Account/Delete?handler=Google", "/Account/Delete", [], GoogleHeaders(_googleId, _email));
        Assert.Equal("login", web.Google.LastPrompt);   // Google se má zeptat znovu
        var confirmed = await client.GetAsync(challenge.Headers.Location);

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Null(await FindUserAsync());
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByLoginAsync(GoogleLogin.Scheme, _googleId));   // propojení smazané s účtem
    }

    [Fact]
    public async Task SmazaniUctuBezHesla_JinymUctemGoogle_Nesmaze()
    {
        var client = web.CreateBrowser();
        await GoogleLoginAsync(client);

        var challenge = await PostAsync(client, "/Account/Delete?handler=Google", "/Account/Delete", [],
            GoogleHeaders(Guid.NewGuid().ToString("N"), $"cizi-{_email}"));
        var confirmed = await client.GetAsync(challenge.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains("Ověření přes Google se nepovedlo", WebUtility.HtmlDecode(await confirmed.Content.ReadAsStringAsync()));
        Assert.NotNull(await FindUserAsync());
    }

    /// <summary>
    /// „Přihlásit přes Google“ jako prohlížeč: formulář na přihlašovací stránce → (falešný) Google → návrat
    /// na ExternalLogin?handler=Callback. Vrací odpověď návratu (přesměrování = přihlášen, 200 = hláška).
    /// </summary>
    private async Task<HttpResponseMessage> GoogleLoginAsync(
        HttpClient client, string? googleId = "", bool emailVerified = true, string returnUrl = "/")
    {
        var id = googleId == "" ? _googleId : googleId;
        var challenge = await PostAsync(client, $"/Account/ExternalLogin?returnUrl={Uri.EscapeDataString(returnUrl)}",
            "/Account/Login", new() { ["provider"] = GoogleLogin.Scheme }, GoogleHeaders(id, _email, emailVerified));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        return await client.GetAsync(challenge.Headers.Location);
    }

    private static Dictionary<string, string> GoogleHeaders(string? id, string email, bool emailVerified = true) =>
        id is null
            ? []
            : new()
            {
                [FakeGoogle.IdHeader] = id,
                [FakeGoogle.EmailHeader] = email,
                [FakeGoogle.EmailVerifiedHeader] = emailVerified ? "true" : "false",
                [FakeGoogle.NameHeader] = "Eva Nováková"
            };

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

    private async Task<ApplicationUser?> FindUserAsync()
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(_email);
    }

    private async Task<ApplicationUser> UserAsync() =>
        await FindUserAsync() ?? throw new InvalidOperationException("Účet neexistuje.");
}
