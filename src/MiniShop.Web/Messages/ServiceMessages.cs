using System.Net;
using MiniShop.Infrastructure.Hosting;
using MiniShop.ServiceDefaults;

namespace MiniShop.Web.Messages;

/// <summary>
/// Mikroservisy M2B: provoz zpráv jedné služby pro stránku /Admin/Messages – Web sám (v procesu) nebo jiná služba
/// přes její <c>/api/messages</c>. Stránka tak ukáže zprávy všech služeb a u každé umí „Zkusit znovu“ a „Vrátit do fronty“.
/// </summary>
public interface IServiceMessages
{
    /// <summary>Jméno služby (jako v Compose): <c>web</c>, <c>inventory-api</c>, <c>reporting-api</c>…</summary>
    string ServiceName { get; }

    /// <summary>Popisek pro člověka.</summary>
    string DisplayName { get; }

    Task<ServiceMessagesStatus> GetAsync(CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(string module, Guid messageId, CancellationToken cancellationToken = default);

    /// <exception cref="ArgumentException">Fronta nepatří této službě.</exception>
    Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default);
}

/// <summary>Moduly, které hostuje Web – přímo, bez HTTP.</summary>
public sealed class LocalServiceMessages(MessagesOverview overview) : IServiceMessages
{
    public string ServiceName => "web";

    public string DisplayName => "Web (objednávky, platby, faktury, e-maily…)";

    public Task<ServiceMessagesStatus> GetAsync(CancellationToken cancellationToken = default) => overview.GetAsync(cancellationToken);

    public Task<bool> RetryAsync(string module, Guid messageId, CancellationToken cancellationToken = default) =>
        overview.RetryAsync(module, messageId, cancellationToken);

    public Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default) =>
        overview.RequeueAsync(queue, cancellationToken);
}

/// <summary>
/// Jiná služba přes <c>/api/messages</c> (servisní klíč té služby přidá <see cref="ServiceApiKeyHandler"/>). Nedostupná
/// služba = výjimka; stránka ji u té služby ukáže jako „nedostupná“ a ostatní služby zobrazí normálně.
/// </summary>
public sealed class RemoteServiceMessages(string serviceName, string displayName, HttpClient http) : IServiceMessages
{
    private const string Messages = "api/messages";

    public string ServiceName => serviceName;

    public string DisplayName => displayName;

    public async Task<ServiceMessagesStatus> GetAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<ServiceMessagesStatus>(Messages, cancellationToken)
        ?? throw new InvalidOperationException($"Prázdná odpověď služby {serviceName}.");

    public async Task<bool> RetryAsync(string module, Guid messageId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(
            $"{Messages}/{Uri.EscapeDataString(module)}/{messageId}/retry", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageRetryResult>(cancellationToken))?.Retried ?? false;
    }

    public async Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(
            $"{Messages}/queues/{Uri.EscapeDataString(queue)}/requeue", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new ArgumentException($"Fronta {queue} nepatří službě {serviceName}.", nameof(queue));
        }

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MessageRequeueResult>(cancellationToken))?.Moved ?? 0;
    }
}

public static class ServiceMessagesRegistration
{
    /// <summary>
    /// Web v procesu + sklad a přehled tržeb přes HTTP (adresy a klíče jako jejich klienti – InventoryApiClient,
    /// ReportingApiClient). Nová služba se zprávami = řádek tady (a <c>MessagesController</c> v ní).
    /// </summary>
    public static IServiceCollection AddServiceMessages(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IServiceMessages, LocalServiceMessages>();
        AddRemote(services, "inventory-api", "Sklad (inventory-api)",
            ServiceApiKey.Read(key => configuration[key], ServiceApiKey.ConfigurationKey));
        AddRemote(services, "reporting-api", "Přehled tržeb (reporting-api)",
            ServiceApiKey.Read(key => configuration[key], ServiceApiKey.ReportingConfigurationKey));
        return services;
    }

    private static void AddRemote(IServiceCollection services, string serviceName, string displayName, string apiKey)
    {
        var clientName = $"messages-{serviceName}";
        services.AddHttpClient(clientName, http => http.BaseAddress = new Uri($"https+http://{serviceName}"))
            .AddHttpMessageHandler(() => new ServiceApiKeyHandler(apiKey));
        services.AddScoped<IServiceMessages>(sp => new RemoteServiceMessages(serviceName, displayName,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName)));
    }
}
