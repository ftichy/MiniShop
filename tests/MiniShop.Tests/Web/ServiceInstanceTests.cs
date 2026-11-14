using Microsoft.Extensions.Configuration;
using MiniShop.ServiceDefaults;

namespace MiniShop.Tests.Web;

/// <summary>
/// Observabilita G3 (oprava): každá kopie služby má v telemetrii vlastní service.instance.id – jinak se metriky
/// dvou kopií Webu v Grafaně slijí do jedné řady.
/// </summary>
public sealed class ServiceInstanceTests
{
    [Fact]
    public void BezAspire_IdJeJmenoKontejneru()
    {
        Assert.Equal("3f2a9c1b7d4e", ServiceInstance.IdToAdd(Configuration(null), "3f2a9c1b7d4e"));
        Assert.Equal("3f2a9c1b7d4e", ServiceInstance.IdToAdd(Configuration("deployment.environment=prod"), "3f2a9c1b7d4e"));
    }

    [Fact]
    public void Aspire_IdUzNastavil_NicSeNemeni()
    {
        Assert.Null(ServiceInstance.IdToAdd(Configuration("service.instance.id=web-abc, service.namespace=x"), "PC"));
        Assert.Null(ServiceInstance.IdToAdd(Configuration("a=b, service.instance.id=web-abc"), "PC"));
    }

    private static IConfiguration Configuration(string? resourceAttributes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_RESOURCE_ATTRIBUTES"] = resourceAttributes })
            .Build();
}
