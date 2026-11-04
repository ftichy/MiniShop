using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Journeys;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Checkout;

/// <summary>
/// E-shop krok 4: pokladna – souhrn košíku, doprava, adresa a odeslání objednávky.
/// </summary>
/// <remarks>
/// Formulář nese <see cref="CheckoutRequest.CheckoutId"/> (verzi košíku): dvojklik nebo F5 po odeslání vrátí stejnou
/// objednávku a starý formulář po změně košíku v jiné záložce neprojde. Pravidla hlídá doména, stránka jen ukazuje hlášky.
/// </remarks>
/// <remarks>
/// E-shop krok 4B: průběh vyplňování sleduje modul Journeys – skript order-form-tracker.js posílá akce uživatele,
/// stránka sama zapíše, co uviděl po odeslání (<c>ValidationFailed</c>), a vznik objednávky (<c>OrderSubmitted</c>
/// spojí proud s číslem objednávky). Sledování nesmí rozbít objednávku – chyba zápisu jde jen do logu.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
public sealed class IndexModel(ICheckoutService checkout, IJourneyService journeys, ILogger<IndexModel> logger) : PageModel
{
    /// <summary>Jméno formuláře v proudu (FormOpened.Target) – podle něj projekce a přehrávač poznají pokladnu.</summary>
    public const string JourneyFormName = JourneyForms.Checkout;

    public CheckoutDto Summary { get; private set; } = null!;

    [BindProperty]
    public CheckoutRequest Input { get; set; } = new();

    /// <summary>
    /// Proud událostí této návštěvy pokladny (skryté pole). Po neúspěšném odeslání se vrátí zpět, takže oprava
    /// pokračuje ve stejném proudu. Prázdné = prohlížeč bez JavaScriptu (nic se nesleduje).
    /// </summary>
    [BindProperty]
    public Guid JourneyId { get; set; }

    /// <summary>Čas odeslání od otevření pokladny (vyplní skript) – od něj pokračují události serveru.</summary>
    [BindProperty]
    public long JourneyOffsetMs { get; set; }

    /// <summary>Hláška pro stránku Objednávky (stejný klíč TempData jako její <c>StatusMessage</c>).</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Summary.Cart.IsEmpty)
        {
            return RedirectToPage("/Cart/Index");
        }

        // Předvyplnit z poslední objednávky kurýrem – kdo jednou dal adresu, nejspíš chce znovu stejnou.
        Input.ShippingMethod = Summary.LastAddress is null ? ShippingMethod.Pickup : ShippingMethod.Courier;
        Input.PaymentMethod = PaymentMethod.Card;   // e-shop krok 8: výchozí platba kartou
        Input.Street = Summary.LastAddress?.Street;
        Input.City = Summary.LastAddress?.City;
        Input.PostalCode = Summary.LastAddress?.PostalCode;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Události serveru se zařadí hned za odeslání formuláře (SubmitClicked má čas JourneyOffsetMs).
        var serverOffsetMs = JourneyOffsetMs + 1;

        if (ModelState.IsValid)
        {
            try
            {
                var order = await checkout.PlaceOrderAsync(Input, User.ToOrderCustomer(), cancellationToken);
                StatusMessage = $"Objednávka č. {order.Id} přijata (celkem {order.TotalPrice:N2} Kč), ověřujeme sklad…";
                await TrackAsync(() => journeys.RecordOrderSubmittedAsync(JourneyId, serverOffsetMs, order.Id, cancellationToken));
                return RedirectToPage("/Orders/Detail", new { id = order.Id });   // e-shop krok 5: rovnou detail
            }
            catch (Exception ex) when (ex is DomainException or ConcurrencyConflictException)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        // Hlášky, které zákazník po odeslání uvidí, do proudu – a stránka pak pokračuje za nimi.
        var errors = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        await TrackAsync(() => journeys.RecordValidationFailedAsync(JourneyId, serverOffsetMs, errors, cancellationToken));
        JourneyOffsetMs = serverOffsetMs + 1;

        // Stránka znovu s aktuálním košíkem: zadaná adresa zůstane, verze košíku se obnoví – zákazník vidí,
        // co se změnilo, a další odeslání už platí pro to, co má před sebou.
        await LoadAsync(cancellationToken);
        ModelState.Remove($"{nameof(Input)}.{nameof(CheckoutRequest.CheckoutId)}");
        Input.CheckoutId = Summary.CheckoutId;
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptPricesAsync(CancellationToken cancellationToken)
    {
        await checkout.AcceptCurrentPricesAsync(User.ToCustomerId(), cancellationToken);
        return RedirectToPage();
    }

    /// <summary>E-shop krok 13: výsledek uplatnění kódu (přes přesměrování – formulář objednávky zůstane netknutý).</summary>
    [TempData]
    public string? CouponMessage { get; set; }

    /// <summary>
    /// E-shop krok 13: uplatnit slevový kód. Vlastní formulář – <see cref="Input"/> se tu neověřuje (pole neposílá),
    /// proto rovnou přesměrování s hláškou v TempData.
    /// </summary>
    public async Task<IActionResult> OnPostApplyCouponAsync(string? couponCode, CancellationToken cancellationToken)
    {
        try
        {
            await checkout.ApplyCouponAsync(User.ToCustomerId(), couponCode ?? string.Empty, cancellationToken);
            CouponMessage = $"Slevový kód {couponCode?.Trim().ToUpperInvariant()} je uplatněný.";
        }
        catch (Exception ex) when (ex is DomainException or ConcurrencyConflictException)
        {
            CouponMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveCouponAsync(CancellationToken cancellationToken)
    {
        await checkout.RemoveCouponAsync(User.ToCustomerId(), cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Summary = await checkout.GetAsync(User.ToCustomerId(), cancellationToken);
        Input.CheckoutId = Input.CheckoutId == Guid.Empty ? Summary.CheckoutId : Input.CheckoutId;

        // Nová návštěva pokladny = nový proud. Po chybě odeslání zůstává původní (přišel ve formuláři).
        if (JourneyId == Guid.Empty)
        {
            JourneyId = Guid.NewGuid();
            JourneyOffsetMs = 0;
        }
    }

    /// <summary>Bez skriptu (JourneyId z formuláře prázdné) se nesleduje nic; chyba zápisu objednávku nezastaví.</summary>
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
            logger.LogWarning(ex, "Průběh pokladny (proud {JourneyId}) se nepodařilo zapsat", JourneyId);
        }
    }

    /// <summary>E-shop krok 8: nabídka plateb v pokladně (v pořadí na stránce).</summary>
    public static readonly IReadOnlyList<PaymentMethod> PaymentMethods = [PaymentMethod.Card, PaymentMethod.CashOnDelivery];

    public static string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => "Kartou online (platební brána)",
        PaymentMethod.CashOnDelivery => "Dobírka – platba při převzetí",
        _ => "Nezadáno"
    };

    public static string ShippingName(ShippingMethod method) => method switch
    {
        ShippingMethod.Pickup => "Osobní odběr na prodejně",
        ShippingMethod.Courier => "Kurýr na adresu",
        _ => "Nezadáno"
    };
}
