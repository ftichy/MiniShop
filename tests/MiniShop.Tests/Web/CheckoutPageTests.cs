using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Objednani_PresmerujeNaObjednavky_DruheOdeslaniNezalozi()
    {
        await web.EnsureCatalogProductAsync(401, "Pokladní myš", 250m);
        var client = await CustomerAsync("pokladna-1@example.com");
        await AddToCartAsync(client, 401);
        var (token, checkoutId) = await ReadFormAsync(client);

        var fields = CourierFields(token, checkoutId);
        var first = await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));
        var second = await client.PostAsync("/Checkout", new FormUrlEncodedContent(fields));   // dvojklik / F5

        Assert.Equal(("/Orders", "/Orders"), (first.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString));
        Assert.Equal(1, await OrdersWithCheckoutIdAsync(checkoutId));
        var orders = WebUtility.HtmlDecode(await client.GetStringAsync("/Orders"));
        Assert.Contains("přijata", orders);
        Assert.Contains("Dlouhá 5, 110 00 Praha", orders);
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
        ["Input.Street"] = "Dlouhá 5",
        ["Input.City"] = "Praha",
        ["Input.PostalCode"] = "110 00"
    };

    private async Task<int> OrdersWithCheckoutIdAsync(Guid checkoutId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.CountAsync(o => o.CheckoutId == checkoutId);
    }

    [GeneratedRegex("name=\"Input\\.CheckoutId\" value=\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex CheckoutIdPattern();
}
