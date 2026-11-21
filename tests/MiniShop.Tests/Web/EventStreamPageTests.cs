using System.Net;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Integration.RabbitMq;

namespace MiniShop.Tests.Web;

/// <summary>Proud B1: stránka Proud zpráv – výpis z proudu (podvržená čtečka), filtr objednávky a výpadek RabbitMQ.</summary>
[Collection(WebCollection.Name)]
public sealed class EventStreamPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Provoz_PoutObjednavky_VPoradiProudu_ZFiltru()
    {
        var now = DateTime.UtcNow;
        web.EventStream.Unavailable = false;
        web.EventStream.Entries.Clear();
        web.EventStream.Entries.AddRange(
        [
            new EventStreamEntry(10, now.AddMinutes(-5), "OrderPlacedV2", "a", 77, "{\n  \"OrderId\": 77,\n  \"CustomerEmail\": \"•••\"\n}"),
            new EventStreamEntry(11, now.AddMinutes(-4), "ReserveStockV1", "b", 77, "{ \"OrderId\": 77 }"),
            new EventStreamEntry(12, now.AddMinutes(-4), "OrderPlacedV2", "c", 78, "{ \"OrderId\": 78 }")
        ]);
        var client = await OperationsAsync("proud-provoz@example.com");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/EventStream?order=77&minutes=60"));

        Assert.Contains("data-track=\"event-stream-table\"", html);
        Assert.True(html.IndexOf("OrderPlacedV2", StringComparison.Ordinal) < html.IndexOf("ReserveStockV1", StringComparison.Ordinal));
        Assert.Contains("č. 77", html);
        Assert.DoesNotContain("č. 78", html);   // filtr objednávky
        Assert.Contains("\"CustomerEmail\": \"•••\"", html);
        Assert.Equal(77, web.EventStream.LastQuery!.OrderId);
        Assert.InRange(web.EventStream.LastQuery.FromUtc, now.AddMinutes(-61), now.AddMinutes(-59));
    }

    [Fact]
    public async Task VypadekRabbitMq_Upozorneni_NeChyba()
    {
        web.EventStream.Unavailable = true;
        try
        {
            var client = await OperationsAsync("proud-vypadek@example.com");

            var response = await client.GetAsync("/Admin/EventStream");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-track=\"event-stream-unavailable\"", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            web.EventStream.Unavailable = false;
        }
    }

    private async Task<HttpClient> OperationsAsync(string email)
    {
        await web.CreateWithRolesAsync(email, MiniShopWebFactory.UserPassword, AccountRoles.Operations);
        return await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
    }
}
