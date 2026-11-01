using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MiniShop.Application.Inventory;
using MiniShop.Inventory.Api;
using MiniShop.ServiceDefaults;
using MiniShop.Tests.TestDoubles;
using MiniShop.Tests.Web;

namespace MiniShop.Tests.InventoryApi;

/// <summary>
/// Účty krok 7: služba skladu (celé Inventory.Api v paměti) pustí na /api jen volajícího se servisním klíčem.
/// Health checky zůstávají otevřené (Docker, Caddy, UptimeRobot).
/// </summary>
/// <remarks>
/// Ve stejné kolekci jako testy Webu – obě služby nastavují statický Serilog logger, nesmí běžet souběžně.
/// </remarks>
[Collection(WebCollection.Name)]
public sealed class InventoryApiAuthenticationTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _api;

    public InventoryApiAuthenticationTests(InventoryApiFactory api) => _api = api;

    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", "/api/products" },
        { "GET", "/api/products/1" },
        { "POST", "/api/products" },
        { "PUT", "/api/products/1" },
        { "POST", "/api/products/1/stock" },
        { "DELETE", "/api/products/1" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task BezKlice_401(string method, string url)
    {
        var response = await SendAsync(_api.CreateClient(), method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task SpatnyKlic_401(string method, string url)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, new string('x', 64));

        var response = await SendAsync(client, method, url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SpravnyKlic_ProduktySeVrati()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add(ServiceApiKey.HeaderName, InventoryApiFactory.ApiKey);

        var products = await client.GetFromJsonAsync<List<ProductDto>>("/api/products");

        Assert.NotEmpty(products!);   // seed skladu
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task HealthCheck_BezKlice(string url)
    {
        var response = await _api.CreateClient().GetAsync(url);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url) => method switch
    {
        "GET" => client.GetAsync(url),
        "POST" => client.PostAsJsonAsync(url, new { }),
        "PUT" => client.PutAsJsonAsync(url, new { }),
        "DELETE" => client.DeleteAsync(url),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}

/// <summary>Inventory.Api v paměti: SQLite v paměti, bez workerů (outbox, RabbitMQ).</summary>
public sealed class InventoryApiFactory : WebApplicationFactory<ApiKeyAuthenticationHandler>
{
    public static readonly string ApiKey = new('a', 64);

    private readonly InMemoryDatabase _inventory = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Inventory", _inventory.ConnectionString);
        builder.UseSetting(ServiceApiKey.ConfigurationKey, ApiKey);

        builder.ConfigureTestServices(services =>
        {
            var workers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType?.Namespace?.StartsWith("MiniShop", StringComparison.Ordinal) == true)
                .ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _inventory.Dispose();
    }
}
