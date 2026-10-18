using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Journeys;

namespace MiniShop.Infrastructure.Journeys;

/// <summary>Infrastruktura modulu Journeys (krok 12): databáze journeys.db s proudem událostí uživatele.</summary>
public static class JourneysInfrastructure
{
    public static IServiceCollection AddJourneysInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<JourneysDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IJourneysUnitOfWork>(sp => sp.GetRequiredService<JourneysDbContext>());
        services.AddScoped<IJourneyEventRepository, JourneyEventRepository>();

        return services;
    }
}
