using System.Net;
using MiniShop.Domain.Orders;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 6: stránka Expedice přes skutečné formuláře (pole jako z prohlížeče) – zabalit, odeslat, doručit.
/// Objednávky jsou rovnou potvrzené v databázi (bez ságy); každý test má vlastního zákazníka.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class FulfilmentPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Kuryr_ZabalitOdeslatDorucit_ZakaznikVidiCisloZasilky()
    {
        var (customer, orderId) = await CustomerWithOrderAsync("expedice-1@example.com", ShippingMethod.Courier);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        Assert.Contains($"value=\"{orderId}\"", await admin.GetStringAsync("/Admin/Fulfilment"));   // ve frontě K zabalení

        await PostAsync(admin, "/Admin/Fulfilment?handler=StartPacking", ("ids", orderId.ToString()));
        Assert.Contains("Balíme", await DetailAsync(customer, orderId));

        // Kurýr bez čísla zásilky → hláška, objednávka zůstane v balení.
        await PostAsync(admin, $"/Admin/Fulfilment?handler=Ship&id={orderId}", ("carrier", "PPL"), ("trackingNumber", ""));
        Assert.Contains("vyplňte dopravce a číslo zásilky", Decode(await admin.GetStringAsync("/Admin/Fulfilment")));

        await PostAsync(admin, $"/Admin/Fulfilment?handler=Ship&id={orderId}", ("carrier", "PPL"), ("trackingNumber", "ZAS-42"));
        var shipped = await DetailAsync(customer, orderId);
        Assert.Contains("PPL, číslo zásilky ZAS-42", shipped);
        Assert.DoesNotContain("Zrušit objednávku", shipped);   // po odeslání už zrušit nejde

        await PostAsync(admin, $"/Admin/Fulfilment?handler=Deliver&id={orderId}");
        var delivered = await DetailAsync(customer, orderId);
        Assert.Contains("Objednávka doručena", delivered);
        Assert.Contains("Doručená", delivered);
    }

    [Fact]
    public async Task OsobniOdber_PripravenoKVyzvednuti_BezDopravce()
    {
        var (customer, orderId) = await CustomerWithOrderAsync("expedice-2@example.com", ShippingMethod.Pickup);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        await PostAsync(admin, "/Admin/Fulfilment?handler=StartPacking", ("ids", orderId.ToString()));
        await PostAsync(admin, $"/Admin/Fulfilment?handler=Ship&id={orderId}");

        var detail = await DetailAsync(customer, orderId);
        Assert.Contains("Objednávka je připravená k vyzvednutí", detail);
        Assert.Contains("K vyzvednutí", detail);
    }

    [Fact]
    public async Task ZakaznikZrusilBehemBaleni_AdminDostaneHlaskuMistoOdeslani()
    {
        var (customer, orderId) = await CustomerWithOrderAsync("expedice-3@example.com", ShippingMethod.Courier);
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);
        await PostAsync(admin, "/Admin/Fulfilment?handler=StartPacking", ("ids", orderId.ToString()));

        // Zákazník ruší z detailu (formulář posílá na stránku Objednávky), admin pak zkusí odeslat.
        await PostAsync(customer, $"/Orders?handler=Cancel&id={orderId}");
        await PostAsync(admin, $"/Admin/Fulfilment?handler=Ship&id={orderId}", ("carrier", "PPL"), ("trackingNumber", "1"));

        Assert.Contains($"Č. {orderId}: Odeslat lze jen zabalenou objednávku.", Decode(await admin.GetStringAsync("/Admin/Fulfilment")));
        Assert.Contains("Objednávka zrušena", await DetailAsync(customer, orderId));
    }

    private async Task<(HttpClient Customer, int OrderId)> CustomerWithOrderAsync(string email, ShippingMethod shipping)
    {
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var orderId = await web.CreateConfirmedOrderForAsync(email, shipping);
        return (await web.LoginAsync(email, MiniShopWebFactory.UserPassword), orderId);
    }

    private static async Task<string> DetailAsync(HttpClient client, int orderId) =>
        Decode(await client.GetStringAsync($"/Orders/{orderId}"));

    private static async Task PostAsync(HttpClient client, string url, params (string Name, string Value)[] fields)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await MiniShopWebFactory.GetAntiforgeryTokenAsync(client, "/")
        };
        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        var response = await client.PostAsync(url, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string Decode(string html) => WebUtility.HtmlDecode(html);
}
