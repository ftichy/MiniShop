using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Reporting;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 11: stránka Přehled tržeb pro admina – vykreslí se i nad prázdným čtecím modelem a tlačítko
/// Přestavět uloží žádost o snímky (RebuildSalesReportV1) do outboxu modulu Reporting.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class SalesReportPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Prehled_AdminVidiStrankuAPrestaveniPosleZadost()
    {
        var admin = await web.LoginAsync(MiniShopWebFactory.AdminEmail, MiniShopWebFactory.AdminPassword);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Admin/Sales"));
        Assert.Contains("Přehled tržeb", page);
        Assert.Contains("data-track=\"sales-rebuild\"", page);

        var before = await RebuildRequestsAsync();
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(admin, "/Admin/Sales");
        var response = await admin.PostAsync("/Admin/Sales?handler=Rebuild", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await RebuildRequestsAsync() > before);
    }

    /// <summary>Žádosti o přestavění v outboxu reporting.db (zpracované i čekající – outbox je jen označí).</summary>
    private async Task<int> RebuildRequestsAsync()
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        return db.Set<OutboxMessage>().Count(m => m.Type == "RebuildSalesReportV1");
    }
}
