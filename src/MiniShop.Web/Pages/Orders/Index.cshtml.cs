using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniShop.Application.Inventory;
using MiniShop.Application.Journeys;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;
using MiniShop.Web.Inventory;

namespace MiniShop.Web.Pages.Orders;

public sealed class IndexModel(
    IOrderService orderService,
    IProductService productService,
    CatalogSyncService catalogSync,
    IJourneyService journeyService,
    IAuthorizationService authorization,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>Účty krok 2: formulář nové objednávky jen pro zákazníka (admin neobjednává).</summary>
    public bool CanPlaceOrders { get; private set; }

    /// <summary>Účty krok 2: vyřízení, obnova katalogu a průběh objednávek jen pro admina.</summary>
    public bool CanManageOrders { get; private set; }

    /// <summary>Účty krok 3: kdo objednává – z účtu, jen pro zobrazení nad formulářem.</summary>
    public string CustomerName => User.ToOrderCustomer().Name;

    public string CustomerEmail => User.ToOrderCustomer().Email;

    public IReadOnlyList<OrderDto> Orders { get; private set; } = [];
    public IReadOnlyList<SelectListItem> ProductOptions { get; private set; } = [];

    [BindProperty]
    public NewOrderInput Input { get; set; } = new();

    /// <summary>
    /// Krok 12A: proud událostí této návštěvy formuláře (skryté pole). Po neúspěšném odeslání se vrátí zpět,
    /// takže oprava chyby pokračuje ve stejném proudu. Prázdné = formulář bez JavaScriptu (nic se nesleduje).
    /// </summary>
    [BindProperty]
    public Guid JourneyId { get; set; }

    /// <summary>Krok 12A: čas odeslání od otevření formuláře (vyplní skript) – od něj pokračují události serveru.</summary>
    [BindProperty]
    public long JourneyOffsetMs { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.RequireCustomer))
        {
            return Forbid();
        }

        // Události serveru se zařadí hned za odeslání formuláře (SubmitClicked má čas JourneyOffsetMs).
        var serverOffsetMs = JourneyOffsetMs + 1;

        if (!ModelState.IsValid)
        {
            await RecordValidationFailedAsync(serverOffsetMs, cancellationToken);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var request = new CreateOrderRequest
        {
            Items = [new CreateOrderItemRequest { ProductId = Input.ProductId, Quantity = Input.Quantity }]
        };

        var result = await ExecuteAsync(async () =>
        {
            var order = await orderService.PlaceOrderAsync(request, User.ToOrderCustomer(), cancellationToken);
            StatusMessage = $"Objednávka č. {order.Id} přijata (celkem {order.TotalPrice:N2} Kč), ověřujeme sklad…";
            await TrackAsync(() => journeyService.RecordOrderSubmittedAsync(JourneyId, serverOffsetMs, order.Id, cancellationToken));
        }, cancellationToken);

        if (!ModelState.IsValid)
        {
            // Objednávku odmítla aplikace (např. produkt mezitím zmizel z katalogu) – uživatel to vidí stejně jako chybu formuláře.
            await RecordValidationFailedAsync(serverOffsetMs, cancellationToken);
        }

        return result;
    }

    /// <summary>Krok 12A: hlášky, které uživatel po odeslání uviděl, do proudu – a stránka pak pokračuje za nimi.</summary>
    private async Task RecordValidationFailedAsync(long offsetMs, CancellationToken cancellationToken)
    {
        var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        await TrackAsync(() => journeyService.RecordValidationFailedAsync(JourneyId, offsetMs, errors, cancellationToken));
        JourneyOffsetMs = offsetMs + 1;
    }

    /// <summary>
    /// Sledování nesmí rozbít objednávku: když se zápis do journeys.db nepovede, objednávka platí dál a jen se zaloguje.
    /// Bez skriptu (JourneyId prázdné) se nesleduje nic.
    /// </summary>
    private async Task TrackAsync(Func<Task> record)
    {
        if (JourneyId == Guid.Empty)
        {
            return;
        }

        try
        {
            await record();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Průběh objednávky (proud {JourneyId}) se nepodařilo zapsat", JourneyId);
        }
    }

    /// <summary>Ruční obnova kopie katalogu – požádá sklad o všechny produkty (SyncCatalogV1).</summary>
    public async Task<IActionResult> OnPostSyncCatalogAsync(CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin))
        {
            return Forbid();
        }

        await catalogSync.RequestAsync(cancellationToken);
        StatusMessage = "Požádali jsme sklad o celý katalog, produkty se doplní během pár sekund.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin))
        {
            return Forbid();
        }

        ModelState.Clear();
        return await ExecuteAsync(async () =>
        {
            await orderService.CompleteAsync(id, cancellationToken);
            StatusMessage = $"Objednávka č. {id} byla vyřízena.";
        }, cancellationToken);
    }

    /// <summary>Zrušit smí každý přihlášený – kdo smí rušit kterou objednávku, rozhodnou účty kroky 3 a 4.</summary>
    public Task<IActionResult> OnPostCancelAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ExecuteAsync(async () =>
        {
            await orderService.CancelAsync(id, User.ToOrderAccess(), cancellationToken);
            StatusMessage = $"Objednávka č. {id} byla zrušena, zboží se za chvilku vrátí na sklad.";
        }, cancellationToken);
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    /// <summary>
    /// Krok 10B: nabídka (název, cena) je z vlastní kopie katalogu Orderingu – objednávat jde, i když sklad neběží.
    /// Zásobu umí říct jen sklad (HTTP API); když neodpovídá, nabídka se ukáže bez ní.
    /// </summary>
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanPlaceOrders = await IsAuthorizedAsync(AuthorizationPolicies.RequireCustomer);
        CanManageOrders = await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin);

        // Krok 12A: nová návštěva formuláře = nový proud. Po chybě odeslání zůstává původní (přišel ve formuláři).
        if (JourneyId == Guid.Empty)
        {
            JourneyId = Guid.NewGuid();
            JourneyOffsetMs = 0;
        }

        Orders = await orderService.GetAllAsync(User.ToOrderAccess(), cancellationToken);
        var catalog = await orderService.GetCatalogAsync(cancellationToken);
        CatalogEmpty = catalog.Count == 0;

        IReadOnlyDictionary<int, int>? stock = null;
        try
        {
            stock = (await productService.GetAllAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.StockQuantity);
        }
        catch (InventoryUnavailableException)
        {
            InventoryUnavailable = true;
        }

        ProductOptions = catalog
            .Select(p => new SelectListItem(
                $"{p.Name} – {p.Price:N2} Kč ({StockText(stock, p.ProductId)})", p.ProductId.ToString()))
            .ToList();
    }

    /// <summary>
    /// Účty krok 2: Razor Pages umí [Authorize] jen na celou stránku, tady má ale každý handler jiné pravidlo.
    /// Proto kontrola v kódu – se stejnými policies, aby pravidla zůstala na jednom místě.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(string policy) =>
        (await authorization.AuthorizeAsync(User, policy)).Succeeded;

    private static string StockText(IReadOnlyDictionary<int, int>? stock, int productId) =>
        stock is null ? "zásoba nedostupná"
        : stock.TryGetValue(productId, out var quantity) ? $"skladem {quantity}"
        : "zásoba neznámá";

    /// <summary>Kopie katalogu je prázdná – produkty ze skladu ještě nedorazily (stránka se sama obnovuje).</summary>
    public bool CatalogEmpty { get; private set; }

    /// <summary>Krok 10B: služba skladu neodpověděla – stránka to ukáže, objednávat jde dál.</summary>
    public bool InventoryUnavailable { get; private set; }

    /// <summary>Krok 9: dokud nějaká objednávka čeká na sklad, stránka se sama obnovuje (jednoduchý polling).</summary>
    public bool HasPendingOrders => Orders.Any(o => o.Status == "Pending");

    public static string StatusBadge(string status) => status switch
    {
        "Pending" => "bg-warning text-dark",
        "Confirmed" => "bg-primary",
        "Rejected" => "bg-danger",
        "Completed" => "bg-success",
        _ => "bg-secondary"
    };

    public static string StatusText(string status) => status switch
    {
        "Pending" => "Ověřujeme sklad…",
        "Confirmed" => "Potvrzená",
        "Rejected" => "Zamítnutá",
        "Completed" => "Vyřízená",
        "Cancelled" => "Zrušená",
        _ => status
    };

    /// <summary>Účty krok 3: bez jména a e-mailu – objednává přihlášený zákazník (údaje z účtu).</summary>
    public sealed class NewOrderInput
    {
        [Range(1, int.MaxValue, ErrorMessage = "Vyberte produkt.")]
        [Display(Name = "Produkt")]
        public int ProductId { get; set; }

        [Range(1, 1000, ErrorMessage = "Množství musí být 1–1000.")]
        [Display(Name = "Množství")]
        public int Quantity { get; set; } = 1;
    }
}
