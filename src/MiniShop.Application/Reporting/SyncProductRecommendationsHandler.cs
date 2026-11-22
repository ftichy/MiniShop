using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>
/// E-shop krok 27B: vyřídí <see cref="SyncProductRecommendationsV1"/> – pošle znovu všechna uložená doporučení
/// (<see cref="ProductRecommendationService.RepublishAllAsync"/>). Opakovaný příkaz neuškodí: stejný čas výpočtu = stejný obsah.
/// </summary>
public sealed class SyncProductRecommendationsHandler(ProductRecommendationService recommendations)
    : IIntegrationMessageHandler<SyncProductRecommendationsV1>
{
    public Task HandleAsync(SyncProductRecommendationsV1 command, CancellationToken cancellationToken) =>
        recommendations.RepublishAllAsync(cancellationToken);
}
