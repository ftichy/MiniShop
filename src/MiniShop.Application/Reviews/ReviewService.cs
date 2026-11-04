using MiniShop.Application.Exceptions;
using MiniShop.Contracts.Reviews;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Reviews;

namespace MiniShop.Application.Reviews;

/// <summary>
/// E-shop krok 14: hodnocení produktů. Kdo smí hodnotit, rozhoduje kopie nákupů (<see cref="IPurchaseRepository"/>) –
/// žádný dotaz do Orderingu. Po každé změně se ve STEJNÉ transakci oznámí nový souhrn <see cref="ProductRatingChangedV1"/>
/// (outbox) – výloha ho dostane do kopie katalogu.
/// </summary>
public sealed class ReviewService(
    IReviewRepository reviews,
    IPurchaseRepository purchases,
    IReviewsMessagePublisher publisher,
    IReviewsUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Hodnocení produktu pro jeho stránku; <paramref name="customerId"/> = přihlášený zákazník (smí hodnotit? jeho hodnocení).</summary>
    public async Task<ProductReviewsDto> GetForProductAsync(int productId, string? customerId,
        CancellationToken cancellationToken = default)
    {
        var all = await reviews.GetForProductAsync(productId, cancellationToken);
        var visible = all.Where(r => !r.IsHidden).OrderByDescending(r => r.UpdatedAtUtc).ToList();
        var mine = customerId is null ? null : all.FirstOrDefault(r => r.CustomerId == customerId);
        var canReview = customerId is not null && await purchases.FindReviewableAsync(customerId, productId, cancellationToken) is not null;

        return new ProductReviewsDto(
            Average(visible),
            visible.Count,
            visible.Select(ToDto).ToList(),
            canReview,
            mine is null ? null : ToDto(mine));
    }

    /// <summary>Nové hodnocení, nebo úprava vlastního. Jen kdo produkt dostal a nevrátil.</summary>
    public async Task SubmitAsync(int productId, string customerId, int rating, string? text,
        CancellationToken cancellationToken = default)
    {
        var purchase = await purchases.FindReviewableAsync(customerId, productId, cancellationToken)
            ?? throw new DomainException("Hodnotit můžete jen zboží, které jste koupili a dostali.");

        var all = (await reviews.GetForProductAsync(productId, cancellationToken)).ToList();
        if (all.FirstOrDefault(r => r.CustomerId == customerId) is { } existing)
        {
            existing.Edit(rating, text, UtcNow);
        }
        else
        {
            var review = Review.Create(productId, customerId, purchase.CustomerName ?? string.Empty, rating, text, UtcNow);
            reviews.Add(review);
            all.Add(review);
        }

        await SaveWithSummaryAsync(productId, all, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewAdminDto>> GetLatestAsync(int count = 100, CancellationToken cancellationToken = default) =>
        (await reviews.GetLatestAsync(count, cancellationToken))
            .Select(r => new ReviewAdminDto(r.Id, r.ProductId, r.AuthorName, r.Rating, r.Text, r.UpdatedAtUtc, r.IsHidden))
            .ToList();

    /// <summary>Moderace – skryté hodnocení zmizí ze stránky i z průměru (nový souhrn jde do výlohy).</summary>
    public async Task SetHiddenAsync(int reviewId, bool hidden, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(reviewId, cancellationToken)
            ?? throw new NotFoundException($"Hodnocení {reviewId} neexistuje.");
        review.SetHidden(hidden);
        await SaveWithSummaryAsync(review.ProductId, await reviews.GetForProductAsync(review.ProductId, cancellationToken),
            cancellationToken);
    }

    /// <summary>Po startu: prázdná kopie nákupů (nový modul) → požádat Ordering o snímky všech objednávek.</summary>
    /// <returns>true, když o nákupy požádala.</returns>
    public async Task<bool> SyncPurchasesIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await purchases.AnyAsync(cancellationToken))
        {
            return false;
        }

        publisher.Send(new SyncPurchasesV1(Guid.NewGuid(), UtcNow));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Změna + nový souhrn v outboxu v jedné transakci – souhrn ve výloze nikdy „neuteče“ od hodnocení.</summary>
    private async Task SaveWithSummaryAsync(int productId, IEnumerable<Review> all, CancellationToken cancellationToken)
    {
        var visible = all.Where(r => !r.IsHidden).ToList();
        publisher.Publish(new ProductRatingChangedV1(Guid.NewGuid(), UtcNow, productId, Average(visible), visible.Count));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static decimal Average(IReadOnlyCollection<Review> visible) =>
        visible.Count == 0 ? 0m : decimal.Round((decimal)visible.Average(r => r.Rating), 1, MidpointRounding.AwayFromZero);

    private static ReviewDto ToDto(Review r) => new(r.Id, r.AuthorName, r.Rating, r.Text, r.UpdatedAtUtc);

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

/// <param name="CanReview">Přihlášený zákazník produkt dostal a nevrátil – formulář se ukáže.</param>
/// <param name="Mine">Jeho hodnocení (i skryté), nebo null.</param>
public sealed record ProductReviewsDto(
    decimal AverageRating,
    int RatingCount,
    IReadOnlyList<ReviewDto> Reviews,
    bool CanReview,
    ReviewDto? Mine);

public sealed record ReviewDto(int Id, string AuthorName, int Rating, string Text, DateTime UpdatedAtUtc);

public sealed record ReviewAdminDto(int Id, int ProductId, string AuthorName, int Rating, string Text, DateTime UpdatedAtUtc, bool IsHidden);
