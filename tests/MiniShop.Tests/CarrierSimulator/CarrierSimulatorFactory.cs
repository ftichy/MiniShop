using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiniShop.CarrierSimulator;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.CarrierSimulator;

/// <summary>
/// E-shop krok 26A: simulovaný dopravce v paměti. Čas stojí (<see cref="Time"/>), plánovač neběží sám – test volá
/// <see cref="ProcessAsync"/>; webhooky chytá <see cref="Webhooks"/> místo e-shopu. Data v dočasné složce.
/// </summary>
public sealed class CarrierSimulatorFactory(IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<ShipmentStore>
{
    public static readonly string ApiKey = new('c', 64);
    public static readonly string WebhookSecret = new('w', 64);
    public const string WebhookUrl = "https://minishop.test/api/shipping/carrier-webhook";
    public const int StepSeconds = 30;

    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "minishop-carrier-" + Guid.NewGuid().ToString("N"));

    public FixedTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));

    public RecordingWebhookReceiver Webhooks { get; } = new();

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ShipmentsApi.ApiKeyHeader, ApiKey);
        return client;
    }

    public Task ProcessAsync() => Services.GetRequiredService<WebhookDispatcher>().ProcessAsync(CancellationToken.None);

    /// <summary>Posune čas o <paramref name="steps"/> kroků dopravce a nechá plánovač doběhnout.</summary>
    public async Task AdvanceStepsAsync(int steps = 1)
    {
        Time.Advance(TimeSpan.FromSeconds(StepSeconds * steps));
        await ProcessAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Carrier:ApiKey", ApiKey);
        builder.UseSetting("Carrier:WebhookSecret", WebhookSecret);
        builder.UseSetting("Carrier:WebhookUrl", WebhookUrl);
        builder.UseSetting("Carrier:DataPath", DataPath);
        builder.UseSetting("Carrier:StepSeconds", StepSeconds.ToString());
        builder.UseSetting("Carrier:Chaos:Seed", "26");
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            // Plánovač s časovačem pryč – test ho volá sám (ProcessAsync), aby výsledek nezávisel na skutečném čase.
            var worker = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(CarrierWorker));
            services.Remove(worker);
            services.AddHttpClient(WebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhooks);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(DataPath))
        {
            Directory.Delete(DataPath, recursive: true);
        }
    }
}

/// <summary>„E-shop“, kterému dopravce posílá webhooky: zapisuje je a odpovídá <see cref="NextStatus"/> (výchozí 200).</summary>
public sealed class RecordingWebhookReceiver : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly List<ReceivedWebhook> _received = [];

    public HttpStatusCode NextStatus { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<ReceivedWebhook> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var signature = request.Headers.TryGetValues(WebhookDispatcher.SignatureHeader, out var values) ? values.Single() : "";
        lock (_lock)
        {
            _received.Add(new ReceivedWebhook(request.RequestUri!.ToString(), body, signature, NextStatus));
        }

        return new HttpResponseMessage(NextStatus);
    }
}

public sealed record ReceivedWebhook(string Url, string Body, string Signature, HttpStatusCode Answered);
