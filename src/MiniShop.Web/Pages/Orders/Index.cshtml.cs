using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using MiniShop.Application.Contracts;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Services;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Web.Pages.Orders;

public sealed class IndexModel(IOrderService orderService, IProductService productService) : PageModel
{
    public IReadOnlyList<OrderDto> Orders { get; private set; } = [];
    public IReadOnlyList<SelectListItem> ProductOptions { get; private set; } = [];

    [BindProperty]
    public NewOrderInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var request = new CreateOrderRequest
        {
            CustomerName = Input.CustomerName,
            CustomerEmail = Input.CustomerEmail,
            Items = [new CreateOrderItemRequest { ProductId = Input.ProductId, Quantity = Input.Quantity }]
        };

        return await ExecuteAsync(async () =>
        {
            var order = await orderService.PlaceOrderAsync(request, cancellationToken);
            StatusMessage = $"Objednávka č. {order.Id} přijata (celkem {order.TotalPrice:N2} Kč), ověřujeme sklad…";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ExecuteAsync(async () =>
        {
            await orderService.CompleteAsync(id, cancellationToken);
            StatusMessage = $"Objednávka č. {id} byla vyřízena.";
        }, cancellationToken);
    }

    public Task<IActionResult> OnPostCancelAsync(int id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return ExecuteAsync(async () =>
        {
            await orderService.CancelAsync(id, cancellationToken);
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Orders = await orderService.GetAllAsync(cancellationToken);
        var products = await productService.GetAllAsync(cancellationToken);
        ProductOptions = products
            .Select(p => new SelectListItem($"{p.Name} – {p.Price:N2} Kč (skladem {p.StockQuantity})", p.Id.ToString()))
            .ToList();
    }

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

    public sealed class NewOrderInput
    {
        [Required(ErrorMessage = "Jméno je povinné."), StringLength(100)]
        [Display(Name = "Jméno zákazníka")]
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-mail je povinný."), EmailAddress(ErrorMessage = "Neplatný e-mail.")]
        [Display(Name = "E-mail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Vyberte produkt.")]
        [Display(Name = "Produkt")]
        public int ProductId { get; set; }

        [Range(1, 1000, ErrorMessage = "Množství musí být 1–1000.")]
        [Display(Name = "Množství")]
        public int Quantity { get; set; } = 1;
    }
}
