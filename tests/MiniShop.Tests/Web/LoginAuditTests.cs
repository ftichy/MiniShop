using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;
using MiniShop.Web.Accounts;

namespace MiniShop.Tests.Web;

/// <summary>
/// 2FA krok B: audit přihlášení – každý pokus (heslo, kód z aplikace, záchranný kód, Google) se zapíše bez e-mailu,
/// neznámý e-mail dostane UserId null, admin vidí pokusy na detailu uživatele, uživatel své na stránce účtu,
/// staré záznamy se po 90 dnech smažou a se smazáním účtu zmizí.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class LoginAuditTests(MiniShopWebFactory web)
{
    private const string Password = "heslo-pro-audit-test";
    private readonly string _email = $"audit-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Heslo_UspechISpatneHeslo_ZapisiSeBezEmailu()
    {
        await web.CreateUserAsync(_email, Password);

        await PasswordLoginAsync(_email, "spatne-heslo-123");
        await PasswordLoginAsync(_email, Password);

        var attempts = await AttemptsAsync(_email);
        Assert.Collection(attempts,   // nejnovější první
            ok => Assert.Equal((LoginMethod.Password, LoginResult.Succeeded), (ok.Method, ok.Result)),
            wrong => Assert.Equal((LoginMethod.Password, LoginResult.WrongPassword), (wrong.Method, wrong.Result)));
        Assert.All(attempts, attempt => Assert.DoesNotContain(_email, attempt.ToString()));
    }

    [Fact]
    public async Task NeznamyEmail_ZapisSeBezUctu()
    {
        var unknown = $"nikdo-{Guid.NewGuid():N}@test.local";

        await PasswordLoginAsync(unknown, Password);

        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var last = db.LoginAttempts.Where(attempt => attempt.UserId == null).OrderByDescending(attempt => attempt.Id).First();
        Assert.Equal(LoginResult.WrongPassword, last.Result);
        Assert.False(db.LoginAttempts.Any(attempt => attempt.IpAddress == unknown));   // e-mail se nikam neukládá
    }

    [Fact]
    public async Task Zamek_PoPetSpatnychHeslech_ZapisujeZamceno()
    {
        await web.CreateUserAsync(_email, Password);

        for (var i = 0; i < AccountsInfrastructure.MaxFailedAccessAttempts; i++)
        {
            await PasswordLoginAsync(_email, "spatne-heslo-123");
        }

        await PasswordLoginAsync(_email, Password);   // správné heslo na zamčený účet

        var attempts = await AttemptsAsync(_email);
        Assert.Equal(LoginResult.LockedOut, attempts[0].Result);
        Assert.Equal(LoginResult.LockedOut, attempts[1].Result);   // pátý špatný pokus zámek spustil
        Assert.Equal(LoginResult.WrongPassword, attempts[2].Result);
    }

    [Fact]
    public async Task ZablokovanyAdminem_ZapisujeZablokovano()
    {
        await web.CreateUserAsync(_email, Password);
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(MiniShopWebFactory.AdminEmail);
            var user = await users.FindByEmailAsync(_email);
            Assert.True((await scope.ServiceProvider.GetRequiredService<AccountAdministration>().LockAsync(admin!.Id, user!.Id)).Succeeded);
        }

        await PasswordLoginAsync(_email, Password);

        Assert.Equal(LoginResult.Blocked, (await AttemptsAsync(_email))[0].Result);
    }

    [Fact]
    public async Task Dvoufazove_HesloKodZachrannyKod_KazdyKrokMaZaznam()
    {
        await web.CreateUserAsync(_email, Password);
        string key, recoveryCode;
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(_email))!;
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            key = (await users.GetAuthenticatorKeyAsync(user))!;
            recoveryCode = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.First();
        }

        // Špatný kód, pak správný.
        var browser = web.CreateBrowser();
        await PostAsync(browser, "/Account/Login", new() { ["Input.Email"] = _email, ["Input.Password"] = Password });
        await PostAsync(browser, "/Account/LoginWith2fa", new() { ["Input.Code"] = "000000" });
        await PostAsync(browser, "/Account/LoginWith2fa", new() { ["Input.Code"] = TestTotp.Compute(key) });

        // Záchranný kód z jiného „zařízení“.
        var other = web.CreateBrowser();
        await PostAsync(other, "/Account/Login", new() { ["Input.Email"] = _email, ["Input.Password"] = Password });
        await PostAsync(other, "/Account/LoginWithRecoveryCode", new() { ["Input.RecoveryCode"] = recoveryCode });

        var attempts = (await AttemptsAsync(_email)).AsEnumerable().Reverse().Select(a => (a.Method, a.Result)).ToList();
        Assert.Equal(
        [
            (LoginMethod.Password, LoginResult.AwaitingSecondFactor),
            (LoginMethod.AuthenticatorCode, LoginResult.WrongCode),
            (LoginMethod.AuthenticatorCode, LoginResult.Succeeded),
            (LoginMethod.Password, LoginResult.AwaitingSecondFactor),
            (LoginMethod.RecoveryCode, LoginResult.Succeeded)
        ], attempts);
    }

    [Fact]
    public async Task Google_UspesnePrihlaseni_MaZaznam()
    {
        await web.CreateUserAsync(_email, Password);
        var browser = web.CreateBrowser();
        var googleId = Guid.NewGuid().ToString("N");

        var challenge = await PostAsync(browser, "/Account/ExternalLogin?returnUrl=%2F", new() { ["provider"] = GoogleLogin.Scheme }, "/Account/Login",
            new()
            {
                [FakeGoogle.IdHeader] = googleId,
                [FakeGoogle.EmailHeader] = _email,
                [FakeGoogle.EmailVerifiedHeader] = "true",
                [FakeGoogle.NameHeader] = "Test"
            });
        await browser.GetAsync(challenge.Headers.Location);

        var attempts = await AttemptsAsync(_email);
        Assert.Equal((LoginMethod.Google, LoginResult.Succeeded), (attempts[0].Method, attempts[0].Result));
    }

    [Fact]
    public async Task AdminVidiPokusyNaDetailu_UzivatelJenSve()
    {
        var other = $"jiny-{Guid.NewGuid():N}@test.local";
        await web.CreateUserAsync(_email, Password);
        await web.CreateUserAsync(other, Password);
        await PasswordLoginAsync(_email, "spatne-heslo-123");
        await PasswordLoginAsync(other, "spatne-heslo-123");
        var userId = await UserIdAsync(_email);

        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var detail = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Admin/Users/Detail/{userId}"));
        Assert.Contains("data-track=\"login-attempts\"", detail);
        Assert.Contains("špatné heslo", detail);

        var own = await web.LoginAsync(_email, Password);
        var page = WebUtility.HtmlDecode(await own.GetStringAsync("/Account/LoginHistory"));
        Assert.Contains("špatné heslo", page);
        Assert.Equal(2, CountRows(page));   // špatné heslo + právě provedené přihlášení; pokusy cizího účtu ne

        Assert.Equal(HttpStatusCode.Redirect, (await web.CreateBrowser().GetAsync("/Account/LoginHistory")).StatusCode);
    }

    [Fact]
    public async Task Retence_StareZaznamySeMazouNoveZustanou()
    {
        await web.CreateUserAsync(_email, Password);
        var userId = await UserIdAsync(_email);
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = DateTime.UtcNow;
        db.LoginAttempts.Add(new LoginAttempt(userId, LoginMethod.Password, LoginResult.WrongPassword,
            now - LoginAttemptRecorder.Retention - TimeSpan.FromDays(1), "203.0.113.7"));
        db.LoginAttempts.Add(new LoginAttempt(userId, LoginMethod.Password, LoginResult.Succeeded, now, "203.0.113.7"));
        await db.SaveChangesAsync();

        var deleted = await scope.ServiceProvider.GetRequiredService<LoginAttemptRecorder>().PurgeExpiredAsync();

        Assert.True(deleted >= 1);
        var left = await scope.ServiceProvider.GetRequiredService<LoginAttemptRecorder>().GetRecentAsync(userId, 10);
        Assert.Single(left);
        Assert.Equal("203.0.113.7", left[0].IpAddress);
    }

    [Fact]
    public async Task SmazaniUctu_SmazePokusy()
    {
        await web.CreateUserAsync(_email, Password);
        var client = await web.LoginAsync(_email, Password);
        var userId = await UserIdAsync(_email);
        Assert.NotEmpty(await AttemptsAsync(_email));

        var response = await PostAsync(client, "/Account/Delete", new() { ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var scope = web.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LoginAttemptRecorder>().GetRecentAsync(userId, 10));
    }

    private static int CountRows(string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, "<tr>\\s*<td class=\"text-nowrap\">").Count;

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<IReadOnlyList<LoginAttemptItem>> AttemptsAsync(string email)
    {
        var userId = await UserIdAsync(email);
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LoginAttemptRecorder>().GetRecentAsync(userId, 50);
    }

    private Task PasswordLoginAsync(string email, string password) =>
        PostAsync(web.CreateBrowser(), "/Account/Login", new() { ["Input.Email"] = email, ["Input.Password"] = password });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields,
        string? tokenPage = null, Dictionary<string, string>? headers = null)
    {
        fields["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, tokenPage ?? url.Split('?')[0]);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request);
    }
}
