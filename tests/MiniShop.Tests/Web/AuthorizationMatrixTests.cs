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
        { Who.Anonymous, "/Shop", Expect.Allowed },                      // e-shop krok 2: výloha
        { Who.Anonymous, "/Shop?q=mys&category=1&instock=true&sort=PriceDescending&p=2", Expect.Allowed },
        { Who.User, "/Shop", Expect.Allowed },
        { Who.Admin, "/Shop", Expect.Allowed },
        { Who.Anonymous, "/media/products/1-0123456789ab.png", Expect.Allowed },   // e-shop krok 2B: obrázky výlohy
        { Who.Anonymous, "/Shop/1", Expect.Allowed },                       // e-shop krok 14: detail produktu (neexistující = 404)
        { Who.User, "/Shop/1", Expect.Allowed },

        // Účty krok 9: smazání účtu – jen přihlášený (admin stránku uvidí, ale smazat nemůže)
        { Who.Anonymous, "/Account/Delete", Expect.Login },
        { Who.User, "/Account/Delete", Expect.Allowed },
        { Who.Admin, "/Account/Delete", Expect.Allowed },

        // Účty krok 10: přihlášení přes Google – návrat z Googlu veřejný, propojení, nastavení hesla a smazání jen přihlášený
        { Who.Anonymous, "/Account/ExternalLogin?handler=Callback", Expect.Allowed },
        { Who.Anonymous, "/Account/ExternalLogins", Expect.Login },
        { Who.User, "/Account/ExternalLogins", Expect.Allowed },
        { Who.Admin, "/Account/ExternalLogins", Expect.Allowed },
        { Who.Anonymous, "/Account/ExternalLogins?handler=LinkCallback", Expect.Login },
        { Who.User, "/Account/ExternalLogins?handler=LinkCallback", Expect.Allowed },
        { Who.Anonymous, "/Account/SetPassword", Expect.Login },
        { Who.User, "/Account/SetPassword", Expect.Allowed },
        { Who.Anonymous, "/Account/Delete?handler=GoogleConfirmed", Expect.Login },
        { Who.User, "/Account/Delete?handler=GoogleConfirmed", Expect.Allowed },

        // Objednávky: jen přihlášení
        { Who.Anonymous, "/Orders", Expect.Login },
        { Who.User, "/Orders", Expect.Allowed },
        { Who.Admin, "/Orders", Expect.Allowed },
        { Who.Anonymous, "/Orders/1", Expect.Login },                  // e-shop krok 5: detail (cizí = 404, řeší služba)
        { Who.User, "/Orders/1", Expect.Allowed },
        { Who.Admin, "/Orders/1", Expect.Allowed },
        { Who.Anonymous, "/Orders/1/Invoice", Expect.Login },         // e-shop krok 15: PDF faktury (cizí/nevystavená = 404)
        { Who.User, "/Orders/1/Invoice", Expect.Allowed },
        { Who.Admin, "/Orders/1/Invoice", Expect.Allowed },
        { Who.Anonymous, "/Orders/1/CreditNote", Expect.Login },      // e-shop krok 15B: PDF dobropisu (cizí/neexistující = 404)
        { Who.User, "/Orders/1/CreditNote", Expect.Allowed },
        { Who.Admin, "/Orders/1/CreditNote", Expect.Allowed },
        { Who.Anonymous, "/Admin/Invoices", Expect.Login },             // e-shop krok 15: přehled faktur – jen admin
        { Who.User, "/Admin/Invoices", Expect.Denied },
        { Who.Admin, "/Admin/Invoices", Expect.Allowed },
        { Who.Anonymous, "/Admin/CartInsights", Expect.Login },         // e-shop krok 17: statistika košíků z událostí – jen admin
        { Who.User, "/Admin/CartInsights", Expect.Denied },
        { Who.Admin, "/Admin/CartInsights", Expect.Allowed },

        // E-shop krok 3: košík – jen zákazník (admin neobjednává)
        { Who.Anonymous, "/Cart", Expect.Login },
        { Who.User, "/Cart", Expect.Allowed },
        { Who.Admin, "/Cart", Expect.Denied },
        { Who.Anonymous, "/Checkout", Expect.Login },                  // e-shop krok 4: pokladna
        { Who.User, "/Checkout", Expect.Allowed },
        { Who.Admin, "/Checkout", Expect.Denied },
        { Who.Anonymous, "/Pay/1", Expect.Login },                       // e-shop krok 8: stránka brány – jen zákazník
        { Who.User, "/Pay/1", Expect.Allowed },
        { Who.Admin, "/Pay/1", Expect.Denied },

        { Who.Anonymous, "/Account/ChangePassword", Expect.Login },
        { Who.User, "/Account/ChangePassword", Expect.Allowed },
        { Who.Anonymous, "/Account/EmailPreferences", Expect.Login },      // e-shop krok 10: souhlas s připomínkou košíku
        { Who.User, "/Account/EmailPreferences", Expect.Allowed },
        { Who.Admin, "/Account/EmailPreferences", Expect.Denied },

        // Jen admin
        { Who.Anonymous, "/Admin/Messages", Expect.Login },               // EDA krok 13: provoz zpráv
        { Who.User, "/Admin/Messages", Expect.Denied },
        { Who.Admin, "/Admin/Messages", Expect.Allowed },
        { Who.Anonymous, "/Admin/Reviews", Expect.Login },                // e-shop krok 14: moderace hodnocení
        { Who.User, "/Admin/Reviews", Expect.Denied },
        { Who.Admin, "/Admin/Reviews", Expect.Allowed },
        { Who.Anonymous, "/Admin/Coupons", Expect.Login },                // e-shop krok 13: slevové kódy
        { Who.User, "/Admin/Coupons", Expect.Denied },
        { Who.Admin, "/Admin/Coupons", Expect.Allowed },
        { Who.Anonymous, "/Admin/Returns", Expect.Login },                // e-shop krok 12: vratky (příjem zboží)
        { Who.User, "/Admin/Returns", Expect.Denied },
        { Who.Admin, "/Admin/Returns", Expect.Allowed },
        { Who.Anonymous, "/Admin/Sales", Expect.Login },                  // e-shop krok 11: přehled tržeb (CQRS)
        { Who.User, "/Admin/Sales", Expect.Denied },
        { Who.Admin, "/Admin/Sales", Expect.Allowed },
        { Who.Anonymous, "/Products", Expect.Login },
        { Who.User, "/Products", Expect.Denied },
        { Who.Admin, "/Products", Expect.Allowed },
        { Who.Anonymous, "/Products/Edit/1", Expect.Login },              // e-shop krok 2: úprava produktu
        { Who.User, "/Products/Edit/1", Expect.Denied },
        { Who.Admin, "/Products/Edit/1", Expect.Allowed },
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

        // E-shop krok 6: expedice – jen admin
        { Who.Anonymous, "/Admin/Fulfilment", Expect.Login },
        { Who.User, "/Admin/Fulfilment", Expect.Denied },
        { Who.Admin, "/Admin/Fulfilment", Expect.Allowed },
    };

    public static TheoryData<Who, string, Expect> PageActions() => new()
    {
        // E-shop krok 3: košík – jen zákazník (bez produktu: projde autorizací, hláška na stránce)
        { Who.Anonymous, "/Cart?handler=Add", Expect.Login },
        { Who.User, "/Cart?handler=Add", Expect.Allowed },
        { Who.Admin, "/Cart?handler=Add", Expect.Denied },
        { Who.User, "/Cart?handler=ChangeQuantity", Expect.Allowed },
        { Who.Admin, "/Cart?handler=ChangeQuantity", Expect.Denied },
        { Who.User, "/Cart?handler=Remove", Expect.Allowed },
        { Who.Admin, "/Cart?handler=Remove", Expect.Denied },

        // E-shop krok 4: pokladna – jen zákazník (bez polí: projde autorizací, hláška na stránce)
        { Who.Anonymous, "/Checkout", Expect.Login },
        { Who.User, "/Checkout", Expect.Allowed },
        { Who.Admin, "/Checkout", Expect.Denied },
        { Who.Anonymous, "/Pay/1", Expect.Login },                       // e-shop krok 8: stránka brány – jen zákazník
        { Who.User, "/Pay/1", Expect.Allowed },
        { Who.Admin, "/Pay/1", Expect.Denied },
        { Who.User, "/Pay/1?handler=Pay", Expect.Allowed },              // e-shop krok 8 (cizí/neexistující = 404)
        { Who.Admin, "/Pay/1?handler=Pay", Expect.Denied },
        { Who.User, "/Pay/1?handler=Decline", Expect.Allowed },
        { Who.User, "/Checkout?handler=AcceptPrices", Expect.Allowed },
        { Who.Admin, "/Checkout?handler=AcceptPrices", Expect.Denied },
        { Who.User, "/Checkout?handler=ApplyCoupon", Expect.Allowed },          // e-shop krok 13 (prázdný kód: hláška)
        { Who.Admin, "/Checkout?handler=ApplyCoupon", Expect.Denied },
        { Who.User, "/Checkout?handler=RemoveCoupon", Expect.Allowed },
        { Who.Admin, "/Checkout?handler=RemoveCoupon", Expect.Denied },
        { Who.User, "/Admin/Messages?handler=Retry&module=Ordering&id=00000000-0000-0000-0000-000000000001", Expect.Denied },   // EDA krok 13
        { Who.Admin, "/Admin/Messages?handler=Retry&module=Ordering&id=00000000-0000-0000-0000-000000000001", Expect.Allowed },
        { Who.User, "/Admin/Messages?handler=Requeue&queue=minishop.ordering", Expect.Denied },
        { Who.Admin, "/Admin/Messages?handler=Requeue&queue=minishop.ordering", Expect.Allowed },
        { Who.Anonymous, "/Shop/1?handler=Review", Expect.Login },              // e-shop krok 14: hodnotit jen zákazník
        { Who.User, "/Shop/1?handler=Review", Expect.Allowed },
        { Who.Admin, "/Shop/1?handler=Review", Expect.Denied },
        { Who.User, "/Admin/Reviews?handler=Toggle&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Reviews?handler=Toggle&id=999", Expect.Allowed },
        { Who.User, "/Admin/Coupons?handler=Create", Expect.Denied },           // e-shop krok 13: jen admin
        { Who.Admin, "/Admin/Coupons?handler=Create", Expect.Allowed },
        { Who.User, "/Admin/Coupons?handler=Deactivate&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Coupons?handler=Deactivate&id=999", Expect.Allowed },

        // E-shop krok 6: expedice (místo „Vyřídit“ na stránce Objednávky) – jen admin
        { Who.Anonymous, "/Admin/Fulfilment?handler=StartPacking", Expect.Login },
        { Who.User, "/Admin/Fulfilment?handler=StartPacking", Expect.Denied },
        { Who.Admin, "/Admin/Fulfilment?handler=StartPacking", Expect.Allowed },
        { Who.User, "/Admin/Fulfilment?handler=Ship&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Fulfilment?handler=Ship&id=999", Expect.Allowed },
        { Who.User, "/Admin/Fulfilment?handler=Deliver&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Fulfilment?handler=Deliver&id=999", Expect.Allowed },

        // E-shop krok 12: vratky – převzetí a zamítnutí jen admin (neexistující vratka: projde autorizací, hláška na stránce);
        // žádost o vrácení na detailu jen zákazník (cizí/neexistující = 404, admin = odepřeno)
        { Who.Anonymous, "/Admin/Returns?handler=Accept&id=999", Expect.Login },
        { Who.User, "/Admin/Returns?handler=Accept&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Returns?handler=Accept&id=999", Expect.Allowed },
        { Who.User, "/Admin/Returns?handler=Reject&id=999", Expect.Denied },
        { Who.Admin, "/Admin/Returns?handler=Reject&id=999", Expect.Allowed },
        { Who.Anonymous, "/Orders/999?handler=Return", Expect.Login },
        { Who.User, "/Orders/999?handler=Return", Expect.Allowed },
        { Who.Admin, "/Orders/999?handler=Return", Expect.Denied },

        // Obnovit katalog: jen admin
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

        // Účty krok 10: Google (bez poskytovatele 404 – projde autorizací, nic se nestane)
        { Who.Anonymous, "/Account/ExternalLogin", Expect.Allowed },
        { Who.Anonymous, "/Account/ExternalLogins?handler=Link", Expect.Login },
        { Who.User, "/Account/ExternalLogins?handler=Link", Expect.Allowed },
        { Who.Anonymous, "/Account/ExternalLogins?handler=Unlink", Expect.Login },
        { Who.User, "/Account/ExternalLogins?handler=Unlink", Expect.Allowed },
        { Who.Anonymous, "/Account/SetPassword", Expect.Login },
        { Who.User, "/Account/SetPassword", Expect.Allowed },
        { Who.Anonymous, "/Account/Delete?handler=Google", Expect.Login },
        { Who.User, "/Account/Delete?handler=Google", Expect.Allowed },

        // Produkty a sklad: jen admin
        { Who.User, "/Products?handler=AddStock&id=1&quantity=5", Expect.Denied },
        { Who.Admin, "/Products?handler=AddStock&id=1&quantity=5", Expect.Allowed },
        { Who.User, "/Products?handler=Delete&id=1", Expect.Denied },
        { Who.Admin, "/Products?handler=Delete&id=1", Expect.Allowed },

        // E-shop krok 2: úprava produktu a kategorie – jen admin
        { Who.User, "/Products/Edit/1", Expect.Denied },
        { Who.Admin, "/Products/Edit/1", Expect.Allowed },
        { Who.User, "/Products?handler=CreateCategory", Expect.Denied },
        { Who.Admin, "/Products?handler=CreateCategory", Expect.Allowed },
        { Who.User, "/Products?handler=RenameCategory&id=1", Expect.Denied },
        { Who.Admin, "/Products?handler=RenameCategory&id=1", Expect.Allowed },
        { Who.User, "/Products?handler=DeleteCategory&id=1", Expect.Denied },
        { Who.Admin, "/Products?handler=DeleteCategory&id=1", Expect.Allowed },

        // E-shop krok 2B: obrázek produktu – jen admin (bez souboru: projde autorizací, hláška na stránce)
        { Who.User, "/Products/Edit/1?handler=Image", Expect.Denied },
        { Who.Admin, "/Products/Edit/1?handler=Image", Expect.Allowed },
        { Who.User, "/Products/Edit/1?handler=RemoveImage", Expect.Denied },
        { Who.Admin, "/Products/Edit/1?handler=RemoveImage", Expect.Allowed },

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

        // E-shop krok 8: webhook brány je bez přihlášení (brána účet nemá) – chrání ho podpis HMAC: bez něj 401.
        { Who.Anonymous, "POST", "/api/payments/webhook", Expect.Unauthorized },
        { Who.User, "POST", "/api/payments/webhook", Expect.Unauthorized },
        // E-shop krok 16: webhook Stripu – také bez přihlášení (chrání ho podpis Stripe-Signature); bez Stripu 404.
        { Who.Anonymous, "POST", "/api/payments/stripe-webhook", Expect.Allowed },
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
        Assert.DoesNotContain("data-track=\"nav-fulfilment\"", userHtml);   // e-shop krok 6
        Assert.Contains("data-track=\"nav-fulfilment\"", adminHtml);
    }

    [Fact]
    public async Task StrankaObjednavky_BezFormulare_ZakaznikMaOdkazDoObchodu()
    {
        var userHtml = await (await ClientAsync(Who.User)).GetStringAsync("/Orders");
        var adminHtml = await (await ClientAsync(Who.Admin)).GetStringAsync("/Orders");

        // E-shop krok 4B: objednává se jen přes košík a pokladnu – na stránce Objednávky formulář nemá nikdo.
        Assert.DoesNotContain("data-journey-id", userHtml);
        Assert.DoesNotContain("data-journey-id", adminHtml);
        Assert.Contains("data-track=\"orders-to-shop\"", userHtml);
        Assert.DoesNotContain("data-track=\"orders-to-shop\"", adminHtml);
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
