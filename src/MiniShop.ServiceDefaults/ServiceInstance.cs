using Microsoft.Extensions.Configuration;

namespace MiniShop.ServiceDefaults;

/// <summary>
/// Observabilita G3 (oprava): každá kopie služby musí v telemetrii nést vlastní <c>service.instance.id</c>. Bez něj
/// se dvě kopie Webu v Grafaně tvářily jako jedna – metriky obou kopií se zapisovaly do stejné řady, čítače se
/// střídaly (1, 0, 1…) a Prometheus každé „vynulování“ započítal jako nový přírůstek (nesmyslné špičky, rozbité SLO).
/// </summary>
/// <remarks>
/// Lokálně (Aspire) ID nastavuje AppHost v <c>OTEL_RESOURCE_ATTRIBUTES</c> – pak se nic nemění. V Compose ho nikdo
/// nenastavuje, proto jméno stroje = jméno kontejneru (Docker ho dá každému kontejneru jiné; po nasazení nové).
/// </remarks>
public static class ServiceInstance
{
    public const string AttributeName = "service.instance.id";

    /// <summary>ID, které se má doplnit, nebo null, když ho už nastavil spouštěč (Aspire).</summary>
    public static string? IdToAdd(IConfiguration configuration, string machineName)
    {
        var attributes = configuration["OTEL_RESOURCE_ATTRIBUTES"] ?? string.Empty;
        var alreadySet = attributes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(pair => pair.StartsWith(AttributeName + "=", StringComparison.Ordinal));
        return alreadySet ? null : machineName;
    }
}
