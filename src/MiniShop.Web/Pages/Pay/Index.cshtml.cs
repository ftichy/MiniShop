using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Payments;
using MiniShop.Infrastructure.Payments;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Pay;

/// <summary>
/// E-shop krok 8: stránka SIMULOVANÉ platební brány – v reálu by byla na webu banky. Zákazník tu platbu potvrdí nebo
/// zamítne a „brána“ nám pošle podepsaný webhook (<see cref="FakePaymentGatewaySimulator"/>). Výsledek pak doručí
/// Payments → sága objednávky (asynchronně přes outbox a RabbitMQ), proto zákazníka vracíme na detail objednávky.
/// </summary>
/// <remarks>Jen zákazník (admin neobjednává) a jen svou platbu – cizí se tváří jako neexistující (404).</remarks>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
public sealed class IndexModel(PaymentQueryService payments, FakePaymentGatewaySimulator gateway) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int OrderId { get; set; }

    /// <summary>Null = platba ještě nevznikla (sága rezervuje zboží) – stránka se za chvíli obnoví.</summary>
    public PaymentDto? Payment { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : NotFound();

    public Task<IActionResult> OnPostPayAsync(CancellationToken cancellationToken) =>
        SimulateAsync(paid: true, cancellationToken);

    public Task<IActionResult> OnPostDeclineAsync(CancellationToken cancellationToken) =>
        SimulateAsync(paid: false, cancellationToken);

    private async Task<IActionResult> SimulateAsync(bool paid, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Payment is not { Status: "Pending", IsExpired: false } payment)
        {
            return Page();   // nic k placení (už zaplaceno, zamítnuto nebo vypršelo) – stránka to vypíše
        }

        var result = paid
            ? await gateway.PayAsync(payment.PaymentId, cancellationToken)
            : await gateway.DeclineAsync(payment.PaymentId, cancellationToken);

        StatusMessage = result != PaymentWebhookResult.Accepted
            ? "Platbu se nepodařilo zpracovat, zkuste to prosím znovu."
            : paid
                ? "Platba proběhla. Potvrzení objednávky přijde během pár sekund."
                : "Platbu jste zamítli – objednávku zrušíme a zboží vrátíme na sklad.";
        return RedirectToPage("/Orders/Detail", new { id = OrderId });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Payment = await payments.GetForCustomerAsync(OrderId, User.ToCustomerId(), cancellationToken);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
    }
}
