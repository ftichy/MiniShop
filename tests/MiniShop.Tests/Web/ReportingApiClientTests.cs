using System.Net;
using System.Text;
using MiniShop.ServiceDefaults;
using MiniShop.Web.Reporting;

namespace MiniShop.Tests.Web;

/// <summary>
/// Mikroservisy M1: Web čte přehled tržeb ze služby Reporting.Api přes HTTP. Klient musí každou poruchu (chybový status,
/// spadlá síť) převést na <see cref="SalesReportUnavailableException"/> – stránka pak ukáže upozornění, ne chybu 500.
/// Server nahrazuje falešný HttpMessageHandler (bez sítě a bez běžící služby).
/// </summary>
public sealed class ReportingApiClientTests
{
    [Fact]
    public async Task Prehled_PrectePresHttpSeServisnimKlicem()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request =>
        {
            sent = request;
            return Json(HttpStatusCode.OK, """
                {"today":{"day":"2026-11-17","orderCount":2,"revenue":1250.0},
                 "days":[{"day":"2026-11-17","orderCount":2,"revenue":1250.0}],
                 "topProducts":[{"productId":1,"name":"Myš","quantity":3,"revenue":2670.0}],
                 "statusCounts":{"Confirmed":2,"Returned":0},"refundedAmount":0,"lastUpdatedAtUtc":null}
                """);
        });

        var report = await client.GetAsync();

        Assert.Equal((HttpMethod.Get, "http://reporting/api/sales"), (sent!.Method, sent.RequestUri!.ToString()));
        Assert.Equal("tajny-klic-prehledu", sent.Headers.GetValues(ServiceApiKey.HeaderName).Single());
        Assert.Equal((2, 1250m, 2), (report.Today.OrderCount, report.Today.Revenue, report.StatusCounts["Confirmed"]));
    }

    [Fact]
    public async Task Prestaveni_PostNaRebuild()
    {
        HttpRequestMessage? sent = null;
        var client = Client(request =>
        {
            sent = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        await client.RebuildAsync();

        Assert.Equal((HttpMethod.Post, "http://reporting/api/sales/rebuild"), (sent!.Method, sent.RequestUri!.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]   // špatně nastavený klíč = služba „není k dispozici“, ne pád stránky
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ChybovyStatus_JeNedostupna(HttpStatusCode status)
    {
        var client = Client(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<SalesReportUnavailableException>(() => client.GetAsync());
        await Assert.ThrowsAsync<SalesReportUnavailableException>(() => client.RebuildAsync());
    }

    [Fact]
    public async Task SpadlaSit_JeNedostupna()
    {
        var client = Client(_ => throw new HttpRequestException("Connection refused"));

        await Assert.ThrowsAsync<SalesReportUnavailableException>(() => client.GetAsync());
    }

    private static ReportingApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new ServiceApiKeyHandler("tajny-klic-prehledu") { InnerHandler = new FakeHandler(respond) })
        {
            BaseAddress = new Uri("http://reporting/")
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
