using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Coupons;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Coupons;

/// <summary>
/// E-shop krok 13: slevové kódy pro admina – přehled s počtem použití, založení a vypnutí.
/// Počet použití se mění v pokladně (+1) a po zamítnutí či zrušení objednávky (−1, ReleaseCouponHandler).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(CouponService coupons) : PageModel
{
    public IReadOnlyList<CouponDto> Coupons { get; private set; } = [];

    [BindProperty]
    public CreateCouponRequest Input { get; set; } = new();

    /// <summary>Platí do – datum z formuláře (místní den, platí celý).</summary>
    [BindProperty]
    public DateOnly? ValidUntil { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Coupons = await coupons.GetAllAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                // Konec zvoleného dne v místním čase → UTC (kupon „do 31. 12.“ platí celý 31. 12.).
                Input.ValidUntilUtc = ValidUntil?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Local).ToUniversalTime();
                var created = await coupons.CreateAsync(Input, cancellationToken);
                StatusMessage = $"Kupon {created.Code} založen.";
                return RedirectToPage();
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        Coupons = await coupons.GetAllAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            await coupons.DeactivateAsync(id, cancellationToken);
            StatusMessage = "Kupon vypnut – další objednávky ho nepoužijí.";
        }
        catch (Exception ex) when (ex is NotFoundException or ConcurrencyConflictException)
        {
            StatusMessage = ex is NotFoundException ? "Kupon neexistuje." : "Kupon se mezitím použil – zkuste to znovu.";
        }

        return RedirectToPage();
    }

    public static string ValueText(CouponDto c) => c.Kind == CouponKind.Percent ? $"{c.Value:0.##} %" : $"{c.Value:N0} Kč";
}
