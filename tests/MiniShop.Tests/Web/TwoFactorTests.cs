using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// Dvoufázové ověření (TOTP): zapnutí přes stránku (QR + klíč + první kód), přihlášení kódem a záchranným kódem,
/// zámek při špatných kódech, admin si 2FA vypnout nesmí a zařízení si nepamatuje, do logu se kód ani klíč nedostane.
/// Kód si test počítá sám podle RFC 6238 – stejně jako aplikace v telefonu.
/// </summary>
[Collection(WebCollection.Name)]
public sealed partial class TwoFactorTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-2fa-test";

    [Fact]
    public void QrKod_SeZmensiCely_MaViewBoxBezPevneSirky()
    {
        // Pevné width/height bez viewBox = CSS kód ořízne (chybí roh a okraj) a telefon ho nenačte.
        var svg = MiniShop.Web.Accounts.TwoFactorHelper.QrCodeSvg("otpauth://totp/MiniShop:test?secret=ABCDEFGHIJKLMNOP&issuer=MiniShop");

        var root = svg[..svg.IndexOf('>')];
        Assert.StartsWith("<svg", root);
        Assert.Matches(@"viewBox=""0 0 \d+ \d+""", root);
        Assert.DoesNotContain(" width=", root);
        Assert.DoesNotContain(" height=", root);
    }

    [Fact]
    public async Task Zapnuti_StrankaUkazeQrAKlic_PrvniKodZapneAUkazeZachranneKody()
    {
        var email = "2fa-zapnuti@example.com";
        await web.CreateUserAsync(email, Password);
        var client = await web.LoginAsync(email, Password);

        var html = await client.GetStringAsync("/Account/TwoFactor");
        Assert.Contains("<svg", html);                       // QR kód vykreslený na serveru
        var key = ReadKey(html);

        // Špatný kód nezapne nic.
        var wrong = await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor", new() { ["Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.False(await IsEnabledAsync(email));

        var ok = await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor", new() { ["Code"] = Totp(key) });
        var body = await ok.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(await IsEnabledAsync(email));
        Assert.Equal(TwoFactorHelperCodeCount, RecoveryCodes(body).Count);
    }

    [Fact]
    public async Task Prihlaseni_HesloPotomKod_PustiAJenSKodem()
    {
        var (email, key) = await UserWithTwoFactorAsync("2fa-login@example.com");

        var browser = web.CreateBrowser();
        var login = await PasswordLoginAsync(browser, email);
        Assert.Contains("/Account/LoginWith2fa", login.Headers.Location!.OriginalString);

        // Po samotném hesle ještě nejsme přihlášení.
        var before = await browser.GetAsync("/Account/ChangePassword");
        Assert.Contains("/Account/Login", before.Headers.Location!.OriginalString);

        var wrong = await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = "000000" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);

        var good = await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = Totp(key) });
        Assert.Equal(HttpStatusCode.Redirect, good.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Account/ChangePassword")).StatusCode);
    }

    [Fact]
    public async Task Prihlaseni_ZachrannyKod_JdePouzitJenJednou()
    {
        var (email, _, codes) = await UserWithTwoFactorAndCodesAsync("2fa-recovery@example.com");

        var first = web.CreateBrowser();
        await PasswordLoginAsync(first, email);
        var ok = await PostAsync(first, "/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new() { ["Input.RecoveryCode"] = codes[0] });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Account/ChangePassword")).StatusCode);

        var second = web.CreateBrowser();
        await PasswordLoginAsync(second, email);
        var reused = await PostAsync(second, "/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new() { ["Input.RecoveryCode"] = codes[0] });
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);   // zůstane na stránce s chybou
    }

    [Fact]
    public async Task SpatneKody_SePocitajiDoZamku()
    {
        var (email, key) = await UserWithTwoFactorAsync("2fa-zamek@example.com");

        var browser = web.CreateBrowser();
        await PasswordLoginAsync(browser, email);
        for (var attempt = 0; attempt < AccountsInfrastructure.MaxFailedAccessAttempts; attempt++)
        {
            await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = "000000" });
        }

        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByEmailAsync(email))!));

        // Zamčený účet nepustí ani správný kód.
        var good = await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = Totp(key) });
        Assert.NotEqual(HttpStatusCode.OK, good.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Account/ChangePassword")).StatusCode);   // dál nepřihlášený
    }

    [Fact]
    public async Task Zakaznik_VypnutiVyzadujeKod_ZapamatovaniZarizeniJeNabidnuto()
    {
        var (email, key) = await UserWithTwoFactorAsync("2fa-vypnuti@example.com");

        var browser = web.CreateBrowser();
        await PasswordLoginAsync(browser, email);
        Assert.Contains("Input.RememberMachine", await browser.GetStringAsync("/Account/LoginWith2fa"));
        await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = Totp(key) });

        await PostAsync(browser, "/Account/TwoFactor?handler=Disable", "/Account/TwoFactor", new() { ["Code"] = "000000" });
        Assert.True(await IsEnabledAsync(email));

        var off = await PostAsync(browser, "/Account/TwoFactor?handler=Disable", "/Account/TwoFactor", new() { ["Code"] = Totp(key) });
        Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
        Assert.False(await IsEnabledAsync(email));
    }

    [Fact]
    public async Task Admin_VypnoutNesmi_AZarizeniSiNepamatuje()
    {
        const string email = "2fa-admin@example.com";
        await web.CreateUserAsync(email, Password);
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.RemoveFromRoleAsync(user, AccountRoles.User);
            await users.AddToRoleAsync(user, AccountRoles.Admin);
        }

        var client = await web.LoginAsync(email, Password);
        var key = ReadKey(await client.GetStringAsync("/Account/TwoFactor"));
        await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor", new() { ["Code"] = Totp(key) });
        Assert.True(await IsEnabledAsync(email));

        var page = await client.GetStringAsync("/Account/TwoFactor");
        Assert.DoesNotContain("handler=Disable", page);

        var disable = await PostAsync(client, "/Account/TwoFactor?handler=Disable", "/Account/TwoFactor", new() { ["Code"] = Totp(key) });
        Assert.NotEqual(HttpStatusCode.OK, disable.StatusCode);   // 403 / přesměrování na „Přístup odepřen“
        Assert.True(await IsEnabledAsync(email));

        // Přihlášení admina: kód ano, „pamatovat zařízení“ ne.
        var browser = web.CreateBrowser();
        await PasswordLoginAsync(browser, email);
        Assert.DoesNotContain("Input.RememberMachine", await browser.GetStringAsync("/Account/LoginWith2fa"));
    }

    [Fact]
    public async Task DoLogu_NeBiKodAniKlic()
    {
        var (email, key, codes) = await UserWithTwoFactorAndCodesAsync("2fa-log@example.com");
        var browser = web.CreateBrowser();
        await PasswordLoginAsync(browser, email);
        var code = Totp(key);
        await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = "111111" });
        await PostAsync(browser, "/Account/LoginWith2fa", "/Account/LoginWith2fa", new() { ["Input.Code"] = code });

        var log = string.Join('\n', web.Logs.Entries);
        Assert.DoesNotContain(key, log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(codes[0], log);
        Assert.DoesNotContain(email, log, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/Account/LoginWith2fa")]
    [InlineData("/Account/LoginWithRecoveryCode")]
    public async Task StrankyKodu_BezHesla_VratiNaPrihlaseniAleNeJeToZamek(string path)
    {
        var browser = web.CreateBrowser();

        var response = await browser.GetAsync(path);

        // Stránka je veřejná (druhý krok po hesle) – nepřihlášený se vrací sám, ne přes autorizaci (ta by přidala ReturnUrl=…).
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("/Account/Login", location);
        Assert.DoesNotContain("ReturnUrl=%2FAccount%2FLoginWith", location);
    }

    // ---- pomocné ----

    private const int TwoFactorHelperCodeCount = 10;

    private async Task<(string Email, string Key)> UserWithTwoFactorAsync(string email)
    {
        var (_, key, _) = await UserWithTwoFactorAndCodesAsync(email);
        return (email, key);
    }

    private async Task<(string Email, string Key, List<string> Codes)> UserWithTwoFactorAndCodesAsync(string email)
    {
        await web.CreateUserAsync(email, Password);
        var client = await web.LoginAsync(email, Password);
        var key = ReadKey(await client.GetStringAsync("/Account/TwoFactor"));
        var response = await PostAsync(client, "/Account/TwoFactor?handler=Enable", "/Account/TwoFactor", new() { ["Code"] = Totp(key) });
        var codes = RecoveryCodes(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(codes);
        return (email, key, codes);
    }

    private static async Task<HttpResponseMessage> PasswordLoginAsync(HttpClient browser, string email)
    {
        var response = await PostAsync(browser, "/Account/Login", "/Account/Login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string tokenPage, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, tokenPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private async Task<bool> IsEnabledAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.TwoFactorEnabled;
    }

    private static string ReadKey(string html)
    {
        var match = KeyPattern().Match(html);
        Assert.True(match.Success, "Na stránce chybí tajný klíč.");
        return match.Groups[1].Value.Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static List<string> RecoveryCodes(string html)
    {
        var block = RecoveryBlockPattern().Match(html);
        return block.Success
            ? RecoveryItemPattern().Matches(block.Value).Select(match => match.Groups[1].Value.Trim()).ToList()
            : [];
    }

    /// <summary>TOTP podle RFC 6238 (HMAC-SHA1, 30 s, 6 číslic) – totéž, co počítá aplikace v telefonu.</summary>
    private static string Totp(string base32Key)
    {
        var key = Base32Decode(base32Key);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var message = new byte[8];
        for (var index = 7; index >= 0; index--)
        {
            message[index] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    [GeneratedRegex("data-track=\"two-factor-key\">([^<]+)</code>")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("data-track=\"two-factor-recovery-codes\">.*?</ul>", RegexOptions.Singleline)]
    private static partial Regex RecoveryBlockPattern();

    [GeneratedRegex("<li>([^<]+)</li>")]
    private static partial Regex RecoveryItemPattern();
}
