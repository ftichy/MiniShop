using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 8: zpevnění přes skutečné HTTP – dočasný zámek účtu, rate limiting formulářů účtu,
/// bezpečnostní hlavičky a logy bez e-mailů a hesel.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class HardeningTests(MiniShopWebFactory web)
{
    private const string Password = "spravne-heslo-123";
    private const string WrongPassword = "spatne-heslo-456";
    private readonly string _email = $"zpevneni-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task PetSpatnychHesel_UcetSeZamkne_AniSpravneHesloNeprojde()
    {
        await web.CreateUserAsync(_email, Password);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(web.CreateBrowser(), _email, WrongPassword)).StatusCode);
        }

        var locked = await LoginAsync(web.CreateBrowser(), _email, Password);

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);   // žádné přesměrování = nepřihlášen
        // Stejná hláška jako u špatného hesla – neprozradí, že účet existuje a je zamčený.
        Assert.Contains("Neplatný e-mail nebo heslo.", WebUtility.HtmlDecode(await locked.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task SpravneHesloPredZamcenim_VynulujePocitadlo()
    {
        await web.CreateUserAsync(_email, Password);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await LoginAsync(web.CreateBrowser(), _email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(web.CreateBrowser(), _email, Password)).StatusCode);
        await LoginAsync(web.CreateBrowser(), _email, WrongPassword);

        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(web.CreateBrowser(), _email, Password)).StatusCode);
    }

    [Fact]
    public async Task PrilisMnohoOdeslaniFormulare_429()
    {
        // Vlastní instance Webu s limitem 3 (sdílený Web má limit vysoko, aby si testy nepřekážely).
        await using var limited = web.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:AccountForms:PermitLimit", "3"));
        var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            statuses.Add((await LoginAsync(client, _email, WrongPassword)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        // Zobrazení stránky se nepočítá – přihlašovací formulář jde pořád otevřít.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
    }

    [Fact]
    public async Task BezpecnostniHlavicky_NaKazdeStrance()
    {
        var response = await web.CreateBrowser().GetAsync("/Account/Login");

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self';", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task Hsts_MimoVyvoj()
    {
        // HSTS se na „localhost“ záměrně neposílá – proto požadavek jako na skutečnou doménu.
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Host = "minishop.test";

        var response = await web.CreateBrowser().SendAsync(request);

        Assert.StartsWith("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task StrankyNemajiInlineSkripty()
    {
        // CSP script-src 'self' by inline <script> nespustila – v MiniShopu proto žádné nejsou
        // (výjimka: data v <script type="application/json">, ta se nespouští).
        var user = await web.LoginAsync(MiniShopWebFactory.UserEmail, MiniShopWebFactory.UserPassword);

        // Přihlašovací stránky přihlášeného přesměrují dál – ty se čtou bez přihlášení.
        var anonymous = web.CreateBrowser();
        var pages = new[] { "/", "/Orders", "/Docs/eda/inbox" }.Select(url => (Client: user, Url: url))
            .Concat(new[] { "/Account/Login", "/Account/Register" }.Select(url => (Client: anonymous, Url: url)));

        foreach (var (client, url) in pages)
        {
            var html = await client.GetStringAsync(url);
            Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)(?![^>]*application/json)[^>]*>", html);
            Assert.DoesNotContain(" onclick=", html);
        }
    }

    [Fact]
    public async Task Logy_NeobsahujiEmailAniHeslo()
    {
        // Vlastní instance Webu: Serilog ve Webu zapisuje přes statický logger a ten patří vždy naposledy spuštěné
        // aplikaci (jiné testy spouští vlastní instance a při ukončení ho zavřou). Schránka logů je společná.
        await using var app = web.WithWebHostBuilder(_ => { });
        HttpClient Browser() => app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var client = Browser();
        var response = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Register"),
            ["Input.DisplayName"] = "Logovaný",
            ["Input.Email"] = _email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await LoginAsync(Browser(), _email, WrongPassword);
        var anonymous = Browser();
        await anonymous.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(anonymous, "/Account/ForgotPassword"),
            ["Input.Email"] = _email
        }));

        var logs = web.Logs.Entries;
        Assert.Contains(logs, entry => entry.Contains("Neúspěšné přihlášení k účtu"));   // audit je, jen bez e-mailu
        Assert.DoesNotContain(logs, entry => entry.Contains(_email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, entry => entry.Contains(Password) || entry.Contains(WrongPassword));
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Account/Login"),
            ["Input.Email"] = email,
            ["Input.Password"] = password
        }));
}
