using System.Net;
using System.Text.RegularExpressions;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 5: e-maily účtu přes skutečné HTTP – registrace → odkaz pro potvrzení, zapomenuté heslo → odkaz
/// pro nové heslo. Odkazy se berou z „odeslaných“ e-mailů (RecordingEmailSender) a otevírají jako v prohlížeči.
/// </summary>
[Collection(WebCollection.Name)]
public sealed partial class AccountEmailTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-test-123";
    private const string ConfirmBanner = "Potvrďte prosím e-mail";
    private readonly string _email = $"novy-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Registrace_PosleOdkaz_PoPotvrzeniZmiziUpozorneni()
    {
        var client = await RegisterAsync(_email);
        Assert.Contains(ConfirmBanner, await PageAsync(client, "/"));

        var email = Assert.Single(web.Emails.To(_email));
        Assert.Contains("potvrďte e-mail", email.Subject);
        var confirmation = await PageAsync(client, LinkFrom(email.TextBody, "/Account/ConfirmEmail"));

        Assert.Contains("E-mail je potvrzený", confirmation);
        Assert.DoesNotContain(ConfirmBanner, await PageAsync(client, "/"));
    }

    [Fact]
    public async Task PotvrzeniEmailu_PoskozenyOdkaz_Neprojde()
    {
        var client = await RegisterAsync(_email);
        var link = LinkFrom(Assert.Single(web.Emails.To(_email)).TextBody, "/Account/ConfirmEmail");

        var page = await PageAsync(client, Regex.Replace(link, "code=[^&]+", "code=AAAA"));

        Assert.Contains("Odkaz je neplatný", page);
        Assert.Contains(ConfirmBanner, await PageAsync(client, "/"));
    }

    [Fact]
    public async Task PoslatZnovu_PosleDalsiOdkaz()
    {
        var client = await RegisterAsync(_email);

        var response = await client.PostAsync("/Account/ResendConfirmation", Form(
            await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, web.Emails.To(_email).Count);
    }

    [Fact]
    public async Task ZapomenuteHeslo_StejnaOdpoved_EmailJenExistujicimuUctu()
    {
        await web.CreateUserAsync(_email, Password);
        var missing = $"nikdo-{Guid.NewGuid():N}@test.local";

        var forExisting = await ForgotAsync(_email);
        var forMissing = await ForgotAsync(missing);

        Assert.Contains("Pokud účet s tímto e-mailem existuje", forExisting);
        Assert.Contains("Pokud účet s tímto e-mailem existuje", forMissing);
        Assert.Single(web.Emails.To(_email));
        Assert.Empty(web.Emails.To(missing));
    }

    [Fact]
    public async Task NoveHeslo_PodleOdkazu_JdeJenJednou()
    {
        const string newPassword = "uplne-nove-heslo-456";
        await web.CreateUserAsync(_email, Password);
        await ForgotAsync(_email);
        var link = LinkFrom(Assert.Single(web.Emails.To(_email)).TextBody, "/Account/ResetPassword");

        var first = await ResetAsync(link, newPassword);
        var second = await ResetAsync(link, "jeste-jine-heslo-789");

        Assert.Contains("Heslo je změněné", first);
        Assert.Contains("Odkaz je neplatný, už byl použitý nebo vypršel", second);
        await web.LoginAsync(_email, newPassword);   // nové heslo platí (staré by LoginAsync odmítl)
    }

    private async Task<HttpClient> RegisterAsync(string email)
    {
        var client = web.CreateBrowser();
        var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Register"),
            ["Input.DisplayName"] = "Nový zákazník",
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<string> ForgotAsync(string email)
    {
        var client = web.CreateBrowser();
        var response = await client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/ForgotPassword"),
            ["Input.Email"] = email
        }));
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> ResetAsync(string link, string newPassword)
    {
        var client = web.CreateBrowser();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(new Uri("https://localhost"), link).Query);
        var response = await client.PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, link),
            ["Input.Email"] = query["email"]!,
            ["Input.Code"] = query["code"]!,
            ["Input.NewPassword"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword
        }));
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> PageAsync(HttpClient client, string path) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(path));

    /// <summary>Odkaz z textu e-mailu jako cesta v aplikaci (https://localhost/Account/… → /Account/…).</summary>
    private static string LinkFrom(string text, string page)
    {
        var match = LinkPattern().Match(text);
        Assert.True(match.Success, $"V e-mailu není odkaz: {text}");
        var path = new Uri(match.Value).PathAndQuery;
        Assert.StartsWith(page, path);
        return path;
    }

    private static FormUrlEncodedContent Form(string token) =>
        new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();
}
