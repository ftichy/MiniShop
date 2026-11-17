using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Notifications;
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
// E-shop krok 24: stránka pro každého přihlášeného (i zaměstnance bez role Zákazník – oznámení k práci);
// připomínka košíku jen zákazník (ověřuje handler).
[Authorize]
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // formulář účtu – stejný limit jako ostatní
public sealed class EmailPreferencesModel(
    UserManager<ApplicationUser> userManager,
    INotificationsApi notifications,   // mikroservisy M2C: zařízení push a oznámení k práci drží služba Notifications
    PushPublicKey pushKey,
    IAuthorizationService authorization,
    ILogger<EmailPreferencesModel> logger)
    : PageModel
{
    [BindProperty]
    public bool CartReminders { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    /// <summary>Oznámení push jsou na serveru zapnutá (klíče VAPID) – jinak se sekce neukáže.</summary>
    public bool PushEnabled => pushKey.IsConfigured;

    /// <summary>Veřejný klíč VAPID pro prohlížeč (<c>applicationServerKey</c>).</summary>
    public string? PushPublicKey => pushKey.Value;

    public IReadOnlyList<PushDeviceDto> PushDevices { get; private set; } = [];

    /// <summary>Zákazník (smí objednávat) – jen jemu se ukáže připomínka košíku.</summary>
    public bool IsCustomer { get; private set; }

    /// <summary>E-shop krok 24: oznámení k práci, ke kterým má zaměstnanec roli, a jestli jsou zapnutá.</summary>
    public IReadOnlyList<(StaffAlertKind Kind, string Label, bool Enabled)> StaffAlerts { get; private set; } = [];

    /// <summary>Mikroservisy M2C: služba Notifications teď neodpovídá – sekce oznámení ukáže upozornění.</summary>
    public bool NotificationsUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CartReminders = user.CartRemindersConsent;
        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>Připomínka opuštěného košíku – jen zákazník (zaměstnanec bez role Zákazník košík nemá).</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!await IsCustomerAsync())
        {
            return Forbid();
        }

        user.CartRemindersConsent = CartReminders;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Nastavení se nepodařilo uložit, zkuste to prosím znovu.");
            await LoadAsync(cancellationToken);
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
            if (!await notifications.SubscribeDeviceAsync(User.ToCustomerId(), request,
                    PushDeviceLabel.FromUserAgent(Request.Headers.UserAgent), cancellationToken))
            {
                return BadRequest();   // neplatná adresa push služby (pravidla v doméně služby)
            }
        }
        catch (NotificationsUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
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

        try
        {
            await notifications.UnsubscribeDeviceAsync(User.ToCustomerId(), request.Endpoint, cancellationToken);
        }
        catch (NotificationsUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return new NoContentResult();
    }

    /// <summary>E-shop krok 23: odebrat zařízení ze seznamu (např. ztracený telefon).</summary>
    public async Task<IActionResult> OnPostRemoveDeviceAsync(int id, CancellationToken cancellationToken)
    {
        StatusMessage = await TryAsync(() => notifications.RemoveDeviceAsync(User.ToCustomerId(), id, cancellationToken))
            ? "Zařízení jsme odebrali – oznámení na něj už nepošleme."
            : Unavailable;
        return RedirectToPage();
    }

    /// <summary>
    /// E-shop krok 24: oznámení k práci. Zapnout jde jen typy, ke kterým má zaměstnanec roli (z cookie); při odeslání
    /// se role ověřuje znovu v databázi (<see cref="IdentityStaffDirectory"/>).
    /// </summary>
    public async Task<IActionResult> OnPostStaffAlertsAsync(List<StaffAlertKind>? kinds, CancellationToken cancellationToken)
    {
        var allowed = StaffAlertRoles.AllowedFor(User);
        if (allowed.Count == 0)
        {
            return Forbid();
        }

        if (!await TryAsync(() => notifications.SetStaffAlertsAsync(User.ToCustomerId(), kinds ?? [], allowed, cancellationToken)))
        {
            StatusMessage = Unavailable;
            return RedirectToPage();
        }

        logger.LogInformation("Účet {UserId}: oznámení k práci {Kinds}", User.ToCustomerId(),
            string.Join(", ", (kinds ?? []).Where(allowed.Contains)));
        StatusMessage = "Oznámení k práci jsou uložená.";
        return RedirectToPage();
    }

    private const string Unavailable = "Chyba: oznámení teď nejde nastavit – služba oznámení neodpovídá. Zkuste to prosím za chvíli.";

    /// <summary>Mikroservisy M2C: volání služby Notifications – false, když neodpovídá (stránka to řekne, nespadne).</summary>
    private async Task<bool> TryAsync(Func<Task> call)
    {
        try
        {
            await call();
            return true;
        }
        catch (NotificationsUnavailableException ex)
        {
            logger.LogWarning(ex, "[Oznámení] Služba Notifications neodpovídá");
            return false;
        }
    }

    private async Task<bool> IsCustomerAsync() =>
        (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded;

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsCustomer = await IsCustomerAsync();
        try
        {
            PushDevices = await notifications.ListDevicesAsync(User.ToCustomerId(), cancellationToken);
            var allowed = StaffAlertRoles.AllowedFor(User);
            if (allowed.Count > 0)
            {
                var enabled = await notifications.GetStaffAlertsAsync(User.ToCustomerId(), cancellationToken);
                StaffAlerts = allowed.Select(kind => (kind, StaffAlertRoles.Labels[kind], enabled.Contains(kind))).ToList();
            }
        }
        catch (NotificationsUnavailableException ex)
        {
            // Připomínka košíku (účet ve Webu) jde nastavit dál; oznámení až se služba vrátí.
            logger.LogWarning(ex, "[Oznámení] Služba Notifications neodpovídá");
            NotificationsUnavailable = true;
        }
    }

    /// <summary>E-shop krok 23: zkušební oznámení na všechna zařízení – ověření bez objednávky.</summary>
    public async Task<IActionResult> OnPostPushTestAsync(CancellationToken cancellationToken)
    {
        int sent;
        try
        {
            sent = await notifications.SendTestAsync(User.ToCustomerId(), cancellationToken);
        }
        catch (NotificationsUnavailableException)
        {
            StatusMessage = Unavailable;
            return RedirectToPage();
        }

        StatusMessage = sent > 0
            ? $"Zkušební oznámení odešlo (zařízení: {sent}). Mělo by se ukázat do pár sekund."
            : "Nemáte žádné zařízení se zapnutými oznámeními.";
        return RedirectToPage();
    }
}

/// <summary>E-shop krok 23: vypnutí oznámení – prohlížeč pošle svůj endpoint.</summary>
public sealed record PushUnsubscribeRequest(string Endpoint);
