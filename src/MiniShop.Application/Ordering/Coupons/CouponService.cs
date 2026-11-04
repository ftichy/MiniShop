using System.ComponentModel.DataAnnotations;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Coupons;

namespace MiniShop.Application.Ordering.Coupons;

/// <summary>E-shop krok 13: správa slevových kódů pro admina (přehled, založení, vypnutí). Jen admin – hlídá Web.</summary>
public sealed class CouponService(ICouponRepository coupons, IOrderingUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await coupons.GetAllAsync(cancellationToken)).Select(ToDto).ToList();

    public async Task<CouponDto> CreateAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        var coupon = Coupon.Create(request.Code, request.Kind, request.Value, request.ValidUntilUtc,
            request.MinItemsTotal, request.MaxRedemptions, timeProvider.GetUtcNow().UtcDateTime);
        coupons.Add(coupon);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Unikátní index na kódu – stejný kód už existuje (nebo ho právě založil jiný admin).
            throw new DomainException($"Kód {coupon.Code} už existuje.");
        }

        return ToDto(coupon);
    }

    public async Task DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var coupon = await coupons.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException($"Kupon {id} neexistuje.");
        coupon.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static CouponDto ToDto(Coupon c) =>
        new(c.Id, c.Code, c.Kind, c.Value, c.ValidUntilUtc, c.MinItemsTotal, c.MaxRedemptions, c.RedemptionCount, c.IsActive);
}

public sealed record CouponDto(
    int Id,
    string Code,
    CouponKind Kind,
    decimal Value,
    DateTime? ValidUntilUtc,
    decimal MinItemsTotal,
    int? MaxRedemptions,
    int RedemptionCount,
    bool IsActive);

/// <summary>Formulář nového kuponu. Pravidla (tvar kódu, rozsah slevy) hlídá <see cref="Coupon.Create"/>.</summary>
public sealed class CreateCouponRequest
{
    [Required(ErrorMessage = "Zadejte kód.")]
    [Display(Name = "Kód")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Typ slevy")]
    public CouponKind Kind { get; set; } = CouponKind.Percent;

    [Range(0.01, 1_000_000, ErrorMessage = "Zadejte výši slevy.")]
    [Display(Name = "Sleva")]
    public decimal Value { get; set; } = 10;

    [Display(Name = "Platí do")]
    public DateTime? ValidUntilUtc { get; set; }

    [Range(0, 1_000_000)]
    [Display(Name = "Nákup od (Kč)")]
    public decimal MinItemsTotal { get; set; }

    [Range(1, 1_000_000)]
    [Display(Name = "Max. použití")]
    public int? MaxRedemptions { get; set; }
}
