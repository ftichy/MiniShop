using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Reporting;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Infrastructure.Reporting;
using MiniShop.Reporting.Api;
using MiniShop.ServiceDefaults;
using MiniShop.Tests.TestDoubles;
using MiniShop.Tests.Web;

namespace MiniShop.Tests.ReportingApi;

/// <summary>
/// Mikroservisy M1: služba přehledu tržeb (celé Reporting.Api v paměti). /api jen se servisním klíčem Webu,
/// health checky otevřené; přehled jde přes HTTP a přestavění uloží žádost o snímky do outboxu schématu reporting.
/// </summary>
/// <remarks>Ve stejné kolekci jako testy Webu – služby nastavují statický Serilog logger, nesmí běžet souběžně.</remarks>
[Collection(WebCollection.Name)]
public sealed class ReportingApiTests : IClassFixture<ReportingApiFactory>
{
    private readonly ReportingApiFactory _api;

    public ReportingApiTests(ReportingApiFactory api) => _api = api;

    [Theory]
    [InlineData("GET", "/api/sales")]
    [InlineData("POST", "/api/sales/rebuild")]
    [InlineData("GET", "/api/messages")]   // mikroservisy M2B
    public async Task BezKliceNeboSeSpatnym_401(string method, string url)
    {
        var anonymous = _api.CreateClient();
        var wrongKey = _api.CreateClient();
        wrongKey.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, new string('x', 64));

        foreach (var client in new[] { anonymous, wrongKey })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthCheck_BezKlice()
    {
        using var response = await _api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Prehled_SeKlicem_StavyJakoText()
    {
        var report = await _api.CreateAuthorizedClient().GetFromJsonAsync<SalesReportDto>("/api/sales");

        Assert.NotNull(report);
        Assert.Equal(SalesReportService.DaysInChart, report.Days.Count);
        Assert.Contains("Confirmed", report.StatusCounts.Keys);
    }

    [Fact]
    public async Task Prestaveni_UloziZadostOSnimkyDoOutboxu()
    {
        var before = await RebuildRequestsAsync();

        using var response = await _api.CreateAuthorizedClient().PostAsync("/api/sales/rebuild", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before + 1, await RebuildRequestsAsync());
    }

    /// <summary>Mikroservisy M2B: provoz zpráv služby pro /Admin/Messages – jen její modul a jen její fronty.</summary>
    [Fact]
    public async Task Zpravy_SeKlicem_JenModulReporting()
    {
        var client = _api.CreateAuthorizedClient();

        var status = await client.GetFromJsonAsync<ServiceMessagesStatus>("/api/messages");
        using var foreignQueue = await client.PostAsync("/api/messages/queues/minishop.notifications/requeue", content: null);

        Assert.Equal(["Reporting"], status!.Modules.Select(m => m.Module));
        Assert.Equal(HttpStatusCode.BadRequest, foreignQueue.StatusCode);
    }

    private async Task<int> RebuildRequestsAsync()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        return db.Set<OutboxMessage>().Count(m => m.Type == "RebuildSalesReportV1");
    }
}

/// <summary>Reporting.Api v paměti: PostgreSQL z <see cref="TestDatabases"/>, bez workerů (outbox, RabbitMQ).</summary>
public sealed class ReportingApiFactory : WebApplicationFactory<SalesReportOnStartupWorker>
{
    public static readonly string ApiKey = new('b', 64);

    private readonly TestDatabases _databases = new();

    /// <summary>Workery, které služba registruje (před odebráním pro testy) – hlídá je <c>WorkerRegistrationTests</c>.</summary>
    public List<Type> RegisteredWorkers { get; private set; } = [];

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, ApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        _databases.Configure(builder);
        builder.UseSetting(ServiceApiKey.ReportingConfigurationKey, ApiKey);

        builder.ConfigureTestServices(services =>
        {
            RegisteredWorkers = HostedServices.RemoveMiniShopWorkers(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _databases.Dispose();
    }
}
