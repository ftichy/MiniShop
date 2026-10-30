using System.Net;
using System.Net.Http.Json;

namespace MiniShop.Tests.Web;

/// <summary>
/// Účty krok 2: matice oprávnění – pro každou roli a každou stránku / akci / endpoint skutečný HTTP požadavek
/// proti celému Webu. Když se nějaká akce omylem odemkne (nebo zamkne), spadne tenhle test.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class AuthorizationMatrixTests(MiniShopWebFactory web)
{
    public enum Who { Anonymous, User, Admin }

    public enum Expect
    {
        /// <summary>Projde autorizací (výsledek může být 200, 302 dál v aplikaci, 400, 404…).</summary>
        Allowed,
        /// <summary>Stránka: přesměrování na přihlášení.</summary>
        Login,
        /// <summary>Stránka: přesměrování na „Přístup odepřen“.</summary>
        Denied,
        /// <summary>API: 401.</summary>
        Unauthorized,
        /// <summary>API: 403.</summary>
        Forbidden
    }

    public static TheoryData<Who, string, Expect> Pages() => new()
    {
        // Veřejné
        { Who.Anonymous, "/", Expect.Allowed },
        { Who.Anonymous, "/Docs", Expect.Allowed },
        { Who.Anonymous, "/Account/Login", Expect.Allowed },
        { Who.Anonymous, "/Account/Register", Expect.Allowed },
        { Who.Anonymous, "/health", Expect.Allowed },
        { Who.Anonymous, "/Account/ForgotPassword", Expect.Allowed },     // účty krok 5
        { Who.Anonymous, "/Account/ResetPassword", Expect.Allowed },
        { Who.Anonymous, "/Account/ConfirmEmail", Expect.Allowed },
        { Who.Anonymous, "/Privacy", Expect.Allowed },                   // účty krok 9

        // Účty krok 9: smazání účtu – jen přihlášený (admin stránku uvidí, ale smazat nemůže)
        { Who.Anonymous, "/Account/Delete", Expect.Login },
        { Who.User, "/Account/Delete", Expect.Allowed },
        { Who.Admin, "/Account/Delete", Expect.Allowed },

        // Objednávky: jen přihlášení
        { Who.Anonymous, "/Orders", Expect.Login },
        { Who.User, "/Orders", Expect.Allowed },
        { Who.Admin, "/Orders", Expect.Allowed },

        { Who.Anonymous, "/Account/ChangePassword", Expect.Login },
        { Who.User, "/Account/ChangePassword", Expect.Allowed },

        // Jen admin
        { Who.Anonymous, "/Products", Expect.Login },
        { Who.User, "/Products", Expect.Denied },
        { Who.Admin, "/Products", Expect.Allowed },
        { Who.User, "/Orders/Journey/1", Expect.Denied },
        { Who.Admin, "/Orders/Journey/1", Expect.Allowed },
        { Who.User, "/Orders/Replay/1", Expect.Denied },
        { Who.Admin, "/Orders/Replay/1", Expect.Allowed },

        // Účty krok 6: správa uživatelů – jen admin
        { Who.Anonymous, "/Admin/Users", Expect.Login },
        { Who.User, "/Admin/Users", Expect.Denied },
        { Who.Admin, "/Admin/Users", Expect.Allowed },
        { Who.User, "/Admin/Users/Detail/neexistuje", Expect.Denied },
        { Who.Admin, "/Admin/Users/Detail/neexistuje", Expect.Allowed },
    };

    public static TheoryData<Who, string, Expect> PageActions() => new()
    {
        // Založit objednávku: jen zákazník
        { Who.Anonymous, "/Orders?handler=Create", Expect.Login },
        { Who.User, "/Orders?handler=Create", Expect.Allowed },
        { Who.Admin, "/Orders?handler=Create", Expect.Denied },

        // Vyřídit, obnovit katalog: jen admin
        { Who.User, "/Orders?handler=Complete&id=999", Expect.Denied },
        { Who.Admin, "/Orders?handler=Complete&id=999", Expect.Allowed },
        { Who.User, "/Orders?handler=SyncCatalog", Expect.Denied },
        { Who.Admin, "/Orders?handler=SyncCatalog", Expect.Allowed },

        // Zrušit: zatím každý přihlášený (vlastnictví řeší účty kroky 3 a 4)
        { Who.Anonymous, "/Orders?handler=Cancel&id=999", Expect.Login },
        { Who.User, "/Orders?handler=Cancel&id=999", Expect.Allowed },
        { Who.Admin, "/Orders?handler=Cancel&id=999", Expect.Allowed },

        // Účty krok 5: znovu poslat potvrzení e-mailu – jen přihlášený
        { Who.Anonymous, "/Account/ResendConfirmation", Expect.Login },
        { Who.User, "/Account/ResendConfirmation", Expect.Allowed },

        // Účty krok 9: smazání účtu (bez hesla – neprojde validací, nic se nesmaže)
        { Who.Anonymous, "/Account/Delete", Expect.Login },
        { Who.User, "/Account/Delete", Expect.Allowed },

        // Produkty a sklad: jen admin
        { Who.User, "/Products?handler=AddStock&id=1&quantity=5", Expect.Denied },
        { Who.Admin, "/Products?handler=AddStock&id=1&quantity=5", Expect.Allowed },
        { Who.User, "/Products?handler=Delete&id=1", Expect.Denied },
        { Who.Admin, "/Products?handler=Delete&id=1", Expect.Allowed },

        // Účty krok 6: akce na detailu uživatele – jen admin (neexistující ID: projde autorizací, nic nezmění)
        { Who.Anonymous, "/Admin/Users/Detail/neexistuje?handler=Promote", Expect.Login },
        { Who.User, "/Admin/Users/Detail/neexistuje?handler=Promote", Expect.Denied },
        { Who.Admin, "/Admin/Users/Detail/neexistuje?handler=Promote", Expect.Allowed },
        { Who.User, "/Admin/Users/Detail/neexistuje?handler=Demote", Expect.Denied },
        { Who.User, "/Admin/Users/Detail/neexistuje?handler=Lock", Expect.Denied },
        { Who.Admin, "/Admin/Users/Detail/neexistuje?handler=Lock", Expect.Allowed },
        { Who.User, "/Admin/Users/Detail/neexistuje?handler=Unlock", Expect.Denied },
    };

    public static TheoryData<Who, string, string, Expect> Api() => new()
    {
        { Who.Anonymous, "GET", "/api/orders", Expect.Unauthorized },
        { Who.User, "GET", "/api/orders", Expect.Allowed },
        { Who.Admin, "GET", "/api/orders", Expect.Allowed },

        { Who.Anonymous, "POST", "/api/orders", Expect.Unauthorized },
        { Who.User, "POST", "/api/orders", Expect.Allowed },
        { Who.Admin, "POST", "/api/orders", Expect.Forbidden },

        { Who.User, "POST", "/api/orders/999/complete", Expect.Forbidden },
        { Who.Admin, "POST", "/api/orders/999/complete", Expect.Allowed },
        { Who.User, "POST", "/api/orders/999/cancel", Expect.Allowed },

        { Who.Anonymous, "POST", $"/api/journeys/{Guid.Empty}/events", Expect.Unauthorized },
        { Who.User, "POST", $"/api/journeys/{Guid.Empty}/events", Expect.Allowed },

        { Who.User, "GET", "/api/journeys/orders/1", Expect.Forbidden },
        { Who.Admin, "GET", "/api/journeys/orders/1", Expect.Allowed },
        { Who.User, "GET", "/api/journeys/orders/1/replay", Expect.Forbidden },
        { Who.Admin, "GET", "/api/journeys/orders/1/replay", Expect.Allowed },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Stranka(Who who, string url, Expect expect)
    {
        var client = await ClientAsync(who);

        var response = await client.GetAsync(url);

        AssertOutcome(response, expect, $"{who} GET {url}");
    }

    [Theory]
    [MemberData(nameof(PageActions))]
    public async Task AkceNaStrance(Who who, string url, Expect expect)
    {
        var client = await ClientAsync(who);
        // Formulář s platným antiforgery tokenem – jinak by požadavek skončil 400 dřív, než se ověří oprávnění.
        // Nepřihlášený ho bere z přihlašovací stránky, přihlášený z hlavičky (formulář Odhlásit).
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, who == Who.Anonymous ? "/Account/Login" : "/");

        var response = await client.PostAsync(url, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        AssertOutcome(response, expect, $"{who} POST {url}");
    }

    [Theory]
    [MemberData(nameof(Api))]
    public async Task ApiEndpoint(Who who, string method, string url, Expect expect)
    {
        var client = await ClientAsync(who);

        var response = method == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsJsonAsync(url, url.Contains("/journeys/") ? (object)Array.Empty<object>() : new { });

        AssertOutcome(response, expect, $"{who} {method} {url}");
    }

    [Fact]
    public async Task MenuProdukty_JenAdmin()
    {
        var userHtml = await (await ClientAsync(Who.User)).GetStringAsync("/");
        var adminHtml = await (await ClientAsync(Who.Admin)).GetStringAsync("/");

        Assert.DoesNotContain("data-track=\"nav-products\"", userHtml);
        Assert.Contains("data-track=\"nav-products\"", adminHtml);
        Assert.DoesNotContain("data-track=\"nav-users\"", userHtml);   // účty krok 6
        Assert.Contains("data-track=\"nav-users\"", adminHtml);
    }

    [Fact]
    public async Task FormularObjednavky_JenZakaznik()
    {
        var userHtml = await (await ClientAsync(Who.User)).GetStringAsync("/Orders");
        var adminHtml = await (await ClientAsync(Who.Admin)).GetStringAsync("/Orders");

        Assert.Contains("id=\"order-form\"", userHtml);
        Assert.DoesNotContain("id=\"order-form\"", adminHtml);
    }

    private Task<HttpClient> ClientAsync(Who who) => who switch
    {
        Who.User => web.LoginAsync(MiniShopWebFactory.UserEmail, MiniShopWebFactory.UserPassword),
        Who.Admin => web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword),
        _ => Task.FromResult(web.CreateBrowser())
    };

    private static void AssertOutcome(HttpResponseMessage response, Expect expect, string request)
    {
        var status = response.StatusCode;
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        var actual = status switch
        {
            HttpStatusCode.Unauthorized => Expect.Unauthorized,
            HttpStatusCode.Forbidden => Expect.Forbidden,
            HttpStatusCode.Redirect when location.Contains("/Account/Login", StringComparison.Ordinal) => Expect.Login,
            HttpStatusCode.Redirect when location.Contains("/Account/AccessDenied", StringComparison.Ordinal) => Expect.Denied,
            _ => Expect.Allowed
        };

        Assert.True(actual == expect, $"{request}: čekáno {expect}, přišlo {(int)status} {location}");
    }
}
