using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Exceptions;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;
using MiniShop.Web.Notifications;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// E-shop krok 10: nastavení e-mailů – souhlas s připomínkou opuštěného košíku (výchozí vypnuto). Odkaz sem vede
/// i z každé připomínky („vypnout“). Jen zákazník – admin košík nemá.
/// E-shop krok 23: oznámení push na telefonu – zapnutí/vypnutí na tomto zařízení (wwwroot/js/push.js posílá JSON
/// s antiforgery tokenem v hlavičce), seznam zařízení, zkušební oznámení.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // formulář účtu – stejný limit jako ostatní
public sealed class EmailPreferencesModel(
    UserManager<ApplicationUser> userManager,
    PushSubscriptionService pushSubscriptions,
    PushNotifier pushNotifier,
    IPushSender pushSender,
    ILogger<EmailPreferencesModel> logger)
    : PageModel
{
    [BindProperty]
    public bool CartReminders { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Oznámení push jsou na serveru zapnutá (klíče VAPID) – jinak se sekce neukáže.</summary>
    public bool PushEnabled => pushSender.IsEnabled;

    /// <summary>Veřejný klíč VAPID pro prohlížeč (<c>applicationServerKey</c>).</summary>
    public string? PushPublicKey => pushSender.PublicKey;

    public IReadOnlyList<PushDeviceDto> PushDevices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CartReminders = user.CartRemindersConsent;
        PushDevices = await pushSubscriptions.ListAsync(User.ToCustomerId(), cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        user.CartRemindersConsent = CartReminders;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Nastavení se nepodařilo uložit, zkuste to prosím znovu.");
            PushDevices = await pushSubscriptions.ListAsync(User.ToCustomerId(), cancellationToken);
            return Page();
        }

        // Do logu jen ID účtu (účty krok 8), ne e-mail.
        logger.LogInformation("Účet {UserId}: připomínky košíku {State}", user.Id, CartReminders ? "zapnuty" : "vypnuty");
        StatusMessage = CartReminders
            ? "Připomínky košíku jsou zapnuté."
            : "Připomínky košíku jsou vypnuté – žádnou už nepošleme.";
        return RedirectToPage();
    }

    /// <summary>E-shop krok 23: prohlížeč po souhlasu pošle „adresu schránky“ zařízení (endpoint + klíče).</summary>
    public async Task<IActionResult> OnPostPushSubscribeAsync([FromBody] PushSubscribeRequest? request, CancellationToken cancellationToken)
    {
        if (!PushEnabled || request is null)
        {
            return BadRequest();
        }

        try
        {
            await pushSubscriptions.SubscribeAsync(User.ToCustomerId(), request,
                PushDeviceLabel.FromUserAgent(Request.Headers.UserAgent), cancellationToken);
        }
        catch (DomainException)
        {
            return BadRequest();
        }

        // Endpoint (identifikátor zařízení) do logu nikdy – jen ID účtu.
        logger.LogInformation("Účet {UserId}: oznámení push zapnuta na zařízení", User.ToCustomerId());
        return new NoContentResult();
    }

    /// <summary>E-shop krok 23: vypnout oznámení na tomto zařízení.</summary>
    public async Task<IActionResult> OnPostPushUnsubscribeAsync([FromBody] PushUnsubscribeRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return BadRequest();
        }

        await pushSubscriptions.UnsubscribeAsync(User.ToCustomerId(), request.Endpoint, cancellationToken);
        return new NoContentResult();
    }

    /// <summary>E-shop krok 23: odebrat zařízení ze seznamu (např. ztracený telefon).</summary>
    public async Task<IActionResult> OnPostRemoveDeviceAsync(int id, CancellationToken cancellationToken)
    {
        await pushSubscriptions.RemoveAsync(User.ToCustomerId(), id, cancellationToken);
        StatusMessage = "Zařízení jsme odebrali – oznámení na něj už nepošleme.";
        return RedirectToPage();
    }

    /// <summary>E-shop krok 23: zkušební oznámení na všechna zařízení – ověření bez objednávky.</summary>
    public async Task<IActionResult> OnPostPushTestAsync(CancellationToken cancellationToken)
    {
        var sent = await pushNotifier.SendToCustomerAsync(User.ToCustomerId(),
            new PushMessage("MiniShop – zkouška", "Oznámení fungují. Takhle vám dáme vědět o objednávce.",
                "/Account/EmailPreferences", "test"), cancellationToken);
        StatusMessage = sent > 0
            ? $"Zkušební oznámení odešlo (zařízení: {sent}). Mělo by se ukázat do pár sekund."
            : "Nemáte žádné zařízení se zapnutými oznámeními.";
        return RedirectToPage();
    }
}

/// <summary>E-shop krok 23: vypnutí oznámení – prohlížeč pošle svůj endpoint.</summary>
public sealed record PushUnsubscribeRequest(string Endpoint);
