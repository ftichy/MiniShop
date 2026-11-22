using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Reporting;
using MiniShop.Domain.Reporting;

namespace MiniShop.Infrastructure.Reporting;

/// <summary>E-shop krok 27A: doporučení se čtou celá – jeden řádek na produkt, jsou jich desítky až stovky.</summary>
public sealed class ProductRecommendationRepository(ReportingDbContext db) : IProductRecommendationRepository
{
    public async Task<IReadOnlyList<ProductRecommendation>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.ProductRecommendations.ToListAsync(cancellationToken);

    public void Add(ProductRecommendation recommendation) => db.ProductRecommendations.Add(recommendation);
}
