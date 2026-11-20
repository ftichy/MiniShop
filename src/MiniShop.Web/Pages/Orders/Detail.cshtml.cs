using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Application.Ordering;
using MiniShop.Application.Invoicing;
using MiniShop.Application.Ordering.Saga;
using MiniShop.Application.Shipping;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// E-shop krok 5: detail objednávky – položky, doprava, adresa a časová osa stavů („kde je moje objednávka“).
/// </summary>
/// <remarks>
/// Jen přihlášený (fallback policy). Zákazník vidí jen svou objednávku – cizí se tváří jako neexistující (404),
/// admin jakoukoli. Zrušení a vyřízení posílají formuláře na stránku Objednávky (tam jsou handlery i kontrola práv).
/// </remarks>
public sealed class DetailModel(
    IOrderService orderService,
    IAuthorizationService authorization,
    OrderSagaSettings sagaSettings,
    InvoiceService invoices,
    ShipmentQueryService shipments)
    : PageModel
{
    public OrderDetailDto Detail { get; private set; } = null!;

    /// <summary>E-shop krok 26C: průběh zásilky od dopravce (jen objednávky předané dopravci), jinak null.</summary>
    public ShipmentTrackingDto? Shipment { get; private set; }

    public OrderDto Order => Detail.Order;

    /// <summary>Admin: vyřízení a odkazy na průběh zadání (Journeys).</summary>
    public bool CanManageOrders { get; private set; }

    /// <summary>Účty krok 12B: zrušit cizí objednávku, průběh a přehrání – podpora.</summary>
    public bool CanSupport { get; private set; }

    /// <summary>Účty krok 12B: odkaz na Expedici – sklad.</summary>
    public bool CanFulfil { get; private set; }

    /// <summary>
    /// Účty krok 12A: vlastní objednávka přihlášeného zákazníka – vratku a platbu nabízí jen tady (zaměstnanec, který je
    /// i zákazník, je u cizích objednávek jen správa).
    /// </summary>
    public bool IsOwnCustomerOrder { get; private set; }

    /// <summary>Účty krok 12B: objednávka přihlášeného (bez ohledu na role) – vlastní objednávku smí zrušit každý.</summary>
    public bool IsOwnOrder { get; private set; }

    /// <summary>E-shop krok 8: kolik minut má zákazník na platbu kartou (termín ságy).</summary>
    public int PaymentMinutes => (int)sagaSettings.PaymentTimeout.TotalMinutes;

    /// <summary>
    /// Vzhled krok F2: na něco se čeká, výřez se sám obnovuje – sága (sklad, platba), předání dopravci, zásilka u dopravce
    /// (až do doručení / vrácení), vratka. Hotová objednávka se neobnovuje (formulář vratky se nepřepisuje).
    /// </summary>
    public bool IsLive =>
        Order.Status is "Pending" or "AwaitingPayment" or "ReturnRequested"
        || Order.AwaitingShipment
        || (Order.Status == "Shipped" && Order.ShippedByCarrier)
        || Detail.Returns?.Any(r => r.Status == "Requested") == true;

    /// <summary>E-shop krok 15/15B: číslo faktury (null = zatím není) a dobropisy (krok 22: i víc – vratky po částech).</summary>
    public OrderDocumentsDto Documents { get; private set; } = new(null, []);

    /// <summary>Hláška z pokladny („Objednávka č. 5 přijata…“) – pokladna sem po odeslání přesměruje.</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    /// <summary>
    /// E-shop krok 12: zákazník žádá o vrácení doručené objednávky. Jen zákazník a jen svou (cizí = 404);
    /// pravidla (do 14 dní, jednou) hlídá Order.RequestReturn.
    /// E-shop krok 22: <paramref name="quantities"/> = kolik kusů čeho (pole <c>quantities[ID produktu]</c>); bez nich
    /// (starší formulář) = celá objednávka.
    /// </summary>
    public async Task<IActionResult> OnPostReturnAsync(int id, string? reason, Dictionary<int, int>? quantities,
        CancellationToken cancellationToken)
    {
        if (!(await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded)
        {
            return Forbid();
        }

        try
        {
            var returnNumber = 0;
            if (quantities is { Count: > 0 })
            {
                returnNumber = await orderService.RequestReturnAsync(id, User.ToCustomerOrderAccess(), reason ?? string.Empty, quantities,
                    cancellationToken);
            }
            else
            {
                await orderService.RequestReturnAsync(id, User.ToCustomerOrderAccess(), reason ?? string.Empty, cancellationToken);
            }

            StatusMessage = (returnNumber > 0 ? $"Vratku {returnNumber} jsme přijali." : "Žádost o vrácení jsme přijali.")
                + " Pokyny, kam zboží poslat, jsme vám poslali e-mailem.";
            return RedirectToPage(new { id });
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is DomainException or ConcurrencyConflictException)
        {
            ModelState.AddModelError(string.Empty, ex is ConcurrencyConflictException
                ? "Objednávka se mezitím změnila – zkontrolujte ji prosím."
                : ex.Message);
            return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
        }
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            Detail = await orderService.GetDetailAsync(id, User.ToOrderAccess(), cancellationToken);
        }
        catch (NotFoundException)
        {
            return false;
        }

        CanManageOrders = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireOrderOverview)).Succeeded;   // účty krok 12B
        CanSupport = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireSupport)).Succeeded;
        CanFulfil = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireFulfilment)).Succeeded;
        Shipment = Detail.Order.ShippedByCarrier ? await shipments.GetByOrderIdAsync(id, cancellationToken) : null;   // e-shop krok 26C
        IsOwnOrder = Detail.CustomerId == User.FindFirstValue(ClaimTypes.NameIdentifier);
        IsOwnCustomerOrder = IsOwnOrder
            && (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded;
        Documents = await invoices.GetDocumentsAsync(id, await InvoiceAccess.CustomerIdOrAdminAsync(User, authorization), cancellationToken);
        return true;
    }

    /// <summary>Čas přechodu pro člověka; starší objednávky (před e-shop krokem 5) ho u pozdějších stavů nemají.</summary>
    public static string FormatTime(DateTime? changedAtUtc) =>
        changedAtUtc is { } time ? time.ToCzechString() : "čas neznámý (starší objednávka)";
}
