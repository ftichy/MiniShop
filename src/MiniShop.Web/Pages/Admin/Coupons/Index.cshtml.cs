using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Coupons;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Coupons;
using MiniShop.Web.Accounts;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Admin.Coupons;

/// <summary>
/// E-shop krok 13: slevové kódy pro admina – přehled s počtem použití, založení a vypnutí.
/// Počet použití se mění v pokladně (+1) a po zamítnutí či zrušení objednávky (−1, ReleaseCouponHandler).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(CouponService coupons) : PageModel
{
    public PagedList<CouponDto> CouponPage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<CouponDto> Coupons => CouponPage.Items;
    public PagerModel Pager => PagerModel.From(CouponPage, "coupons");

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    [BindProperty]
    public CreateCouponRequest Input { get; set; } = new();

    /// <summary>Platí do – datum z formuláře (místní den, platí celý).</summary>
    [BindProperty]
    public DateOnly? ValidUntil { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>„Platí do“ = konec zvoleného dne v českém čase, převedený na UTC (zóna serveru nehraje roli).</summary>
    public static DateTime EndOfDayUtc(DateOnly day) => CzechTime.ToUtc(day.ToDateTime(TimeOnly.MaxValue));

    // Kuponů jsou jednotky až desítky – stránka v paměti (stejný vzhled jako ostatní seznamy).
    private async Task LoadAsync(CancellationToken cancellationToken) =>
        CouponPage = (await coupons.GetAllAsync(cancellationToken)).ToPagedList(PageRequest.Of(PageNumber, PageSize));

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                // Konec zvoleného dne v českém čase → UTC (kupon „do 31. 12.“ platí celý 31. 12. podle Prahy, ne podle zóny
                // serveru – v kontejneru je UTC, dřív tak kupon platil o hodinu, v létě o dvě déle).
                Input.ValidUntilUtc = ValidUntil is { } day ? EndOfDayUtc(day) : null;
                var created = await coupons.CreateAsync(Input, cancellationToken);
                StatusMessage = $"Kupon {created.Code} založen.";
                return RedirectToPage();
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        await LoadAsync(cancellationToken);
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

        return RedirectToPage(new { p = PageNumber, size = PageSize });   // zpět na stejnou stránku seznamu
    }

    public static string ValueText(CouponDto c) => c.Kind == CouponKind.Percent ? $"{c.Value:0.##} %" : $"{c.Value:N0} Kč";
}
