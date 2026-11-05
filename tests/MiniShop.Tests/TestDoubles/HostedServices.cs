using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Workery MiniShopu v kontejneru služeb – testy služeb v paměti (WebApplicationFactory) je odebírají, testy zpracují
/// outbox ručně. Škálování krok 5: volba vedoucí kopie je registrovaná továrnou (<c>ImplementationType</c> je null),
/// typ se proto bere i z návratového typu továrny.
/// </summary>
public static class HostedServices
{
    public static Type? WorkerType(ServiceDescriptor descriptor) =>
        descriptor.ServiceType != typeof(IHostedService) ? null
            : descriptor.ImplementationType ?? descriptor.ImplementationFactory?.Method.ReturnType;

    public static List<ServiceDescriptor> MiniShopWorkers(IServiceCollection services) =>
        services.Where(d => WorkerType(d)?.Namespace?.StartsWith("MiniShop", StringComparison.Ordinal) == true).ToList();

    public static List<Type> RemoveMiniShopWorkers(IServiceCollection services)
    {
        var workers = MiniShopWorkers(services);
        foreach (var worker in workers)
        {
            services.Remove(worker);
        }

        return workers.Select(w => WorkerType(w)!).ToList();
    }
}
