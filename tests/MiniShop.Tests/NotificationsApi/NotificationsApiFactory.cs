using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Notifications;
using MiniShop.ServiceDefaults;
using MiniShop.Tests.TestDoubles;
using MiniShop.Tests.Web;

namespace MiniShop.Tests.NotificationsApi;

/// <summary>
/// Mikroservisy M2C: služba Notifications v paměti – PostgreSQL z <see cref="TestDatabases"/>, bez workerů (RabbitMQ),
/// oznámení push do <see cref="RecordingPushSender"/>, e-maily do <see cref="RecordingEmailSender"/>. Testy Webu ji volají
/// přes HTTP (<see cref="CreateAuthorizedClient"/>) – celá cesta stránka → API → služba jako v produkci.
/// </summary>
public sealed class NotificationsApiFactory : WebApplicationFactory<MiniShop.Notifications.Api.Controllers.PushController>
{
    public static readonly string ApiKey = new('n', 64);

    private readonly TestDatabases _databases = new();

    public RecordingPushSender Push { get; } = new();

    public RecordingEmailSender Emails { get; } = new();

    /// <summary>Workery, které služba registruje (před odebráním pro testy) – hlídá je <c>WorkerRegistrationTests</c>.</summary>
    public List<Type> RegisteredWorkers { get; private set; } = [];

    public HttpClient CreateAuthorizedClient() => CreateDefaultClient(new ServiceApiKeyHandler(ApiKey));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        _databases.Configure(builder);
        builder.UseSetting(ServiceApiKey.NotificationsConfigurationKey, ApiKey);
        builder.UseSetting(ServiceApiKey.WebInternalConfigurationKey, MiniShopWebFactory.InternalApiKey);
        builder.UseSetting("Notifications:PublicBaseUrl", "https://minishop.test");

        builder.ConfigureTestServices(services =>
        {
            RegisteredWorkers = HostedServices.RemoveMiniShopWorkers(services);
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _databases.Dispose();
    }
}
