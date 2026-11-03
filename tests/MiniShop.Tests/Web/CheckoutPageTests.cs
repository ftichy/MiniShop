using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Journeys;
using MiniShop.Infrastructure.Ordering;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 4: pokladna přes skutečný formulář – pole posílaná stejně jako z prohlížeče (<c>Input.…</c>),
/// včetně skrytého <c>Input.CheckoutId</c>. Každý test má vlastního zákazníka (prázdný košík na začátku).
/// </summary>
[Collection(WebCollection.Name)]
public sealed partial class CheckoutPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Objednani_PresmerujeNaDetail_DruheOdeslaniNezalozi()
    {
        await web.EnsureCatalogProductAsync(401, "Pokladní myš", 250m);
        var client = await CustomerAsync("pokladna-1@example.com");
        await AddToCartAsync(client, 401);
        var (token, checkoutId) = await ReadFormAsync(client);

        var fields = CourierFields(token, checkoutId);
        var first = await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));
        var second = await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));   // dvojklik / F5

        // E-shop krok 5: obě odeslání vedou na detail téže objednávky.
        var location = first.Headers.Location?.OriginalString;
        Assert.Matches("^/Orders/[0-9]+$", location);
        Assert.Equal(location, second.Headers.Location?.OriginalString);
        Assert.Equal(1, await OrdersWithCheckoutIdAsync(checkoutId));
        var detail = WebUtility.HtmlDecode(await client.GetStringAsync(location));
        Assert.Contains("přijata (celkem", detail);                       // hláška z pokladny
        Assert.Contains("Dlouhá 5, 110 00 Praha", detail);
        Assert.Contains("Objednávka přijata, ověřujeme sklad", detail);   // první řádek historie
    }

    [Fact]
    public async Task NeplatnePsc_ChybaNaStranceAKosikZustane()
    {
        await web.EnsureCatalogProductAsync(402, "Pokladní kabel", 90m);
        var client = await CustomerAsync("pokladna-2@example.com");
        await AddToCartAsync(client, 402);
        var (token, checkoutId) = await ReadFormAsync(client);
        var fields = CourierFields(token, checkoutId);
        fields["Input.PostalCode"] = "12";

        var response = await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PSČ musí mít 5 číslic.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Contains("Pokladní kabel", WebUtility.HtmlDecode(await client.GetStringAsync("/Cart")));
    }

    /// <summary>
    /// E-shop krok 4B: průběh pokladny – prohlížeč pošle FormOpened (jako order-form-tracker.js), server zapíše chybu
    /// formuláře a vznik objednávky. Admin pak průběh přehraje na kopii pokladny.
    /// </summary>
    [Fact]
    public async Task Prubeh_ChybaAObjednavkaVProudu_PrehraniUkazePokladnu()
    {
        await web.EnsureCatalogProductAsync(403, "Sledovaná podložka", 120m);
        var client = await CustomerAsync("pokladna-4@example.com");
        await AddToCartAsync(client, 403);
        var html = await client.GetStringAsync("/Checkout");
        Assert.Contains("order-form-tracker.js", html);
        var journeyId = Guid.Parse(JourneyIdPattern().Match(html).Groups[1].Value);
        await client.PostAsJsonAsync($"/api/journeys/{journeyId}/events",
            new[] { new { eventId = Guid.NewGuid(), type = "FormOpened", offsetMs = 0, target = "checkout" } });
        var (token, checkoutId) = await ReadFormAsync(client);

        var fields = CourierFields(token, checkoutId);
        fields["JourneyId"] = journeyId.ToString();
        fields["JourneyOffsetMs"] = "5000";
        fields["Input.PostalCode"] = "12";
        await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));   // chyba PSČ
        fields["Input.PostalCode"] = "110 00";
        fields["JourneyOffsetMs"] = "9000";
        await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));

        var orderId = await OrderIdAsync(checkoutId);
        await using (var scope = web.Services.CreateAsyncScope())
        {
            var journey = await scope.ServiceProvider.GetRequiredService<IJourneyService>().GetByOrderIdAsync(orderId);
            Assert.Equal(JourneyForms.Checkout, journey.Form);
            Assert.Equal(["FormOpened", "ValidationFailed", "OrderSubmitted"], journey.Events.Select(e => e.Type));
        }

        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        var replay = await admin.GetStringAsync($"/Orders/Replay/{orderId}");
        Assert.Contains("data-replay-target=\"Input.Street\"", replay);
        Assert.DoesNotContain("data-replay-target=\"Input.ProductId\"", replay);
    }

    [Fact]
    public async Task PrazdnyKosik_PresmerujeDoKosiku()
    {
        var client = await CustomerAsync("pokladna-3@example.com");

        var response = await client.GetAsync("/Checkout");

        Assert.Equal("/Cart", response.Headers.Location?.OriginalString);
    }

    private async Task<HttpClient> CustomerAsync(string email)
    {
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        return await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
    }

    private static async Task AddToCartAsync(HttpClient client, int productId)
    {
        var response = await client.PostAsync("/Cart?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Shop"),
            ["productId"] = productId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Antiforgery token a verze košíku ze skrytého pole – jako je pošle prohlížeč.</summary>
    private static async Task<(string Token, Guid CheckoutId)> ReadFormAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Checkout");
        var match = CheckoutIdPattern().Match(html);
        Assert.True(match.Success, "Pokladna nemá skryté pole Input.CheckoutId.");
        return (await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/Checkout"), Guid.Parse(match.Groups[1].Value));
    }

    private static Dictionary<string, string> CourierFields(string token, Guid checkoutId) => new()
    {
        ["__RequestVerificationToken"] = token,
        ["Input.CheckoutId"] = checkoutId.ToString(),
        ["Input.ShippingMethod"] = "Courier",
        ["Input.PaymentMethod"] = "CashOnDelivery",   // e-shop krok 8
        ["Input.Street"] = "Dlouhá 5",
        ["Input.City"] = "Praha",
        ["Input.PostalCode"] = "110 00"
    };

    private async Task<int> OrdersWithCheckoutIdAsync(Guid checkoutId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.CountAsync(o => o.CheckoutId == checkoutId);
    }

    private async Task<int> OrderIdAsync(Guid checkoutId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders
            .Where(o => o.CheckoutId == checkoutId).Select(o => o.Id).SingleAsync();
    }

    [GeneratedRegex("name=\"Input\\.CheckoutId\" value=\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex CheckoutIdPattern();

    [GeneratedRegex("name=\"JourneyId\" value=\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex JourneyIdPattern();
}
