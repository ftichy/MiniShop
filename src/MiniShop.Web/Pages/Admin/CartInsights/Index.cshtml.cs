using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.CartInsights;

/// <summary>
/// E-shop krok 17: statistika košíků pro admina – spočítaná při každém zobrazení z událostí košíků (event sourcing).
/// Dřív by šla jen dopředu: tabulka košíku znala jen poslední stav, co zákazník odebral, se nikde neuložilo.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(CartHistoryService history) : PageModel
{
    public CartInsightsDto Insights { get; private set; } = new(null, 0, 0, 0, 0, 0, []);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Insights = await history.GetInsightsAsync(cancellationToken);
}
