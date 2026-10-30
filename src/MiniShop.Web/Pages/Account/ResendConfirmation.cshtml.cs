using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>Účty krok 5: znovu poslat odkaz pro potvrzení e-mailu přihlášenému uživateli (tlačítko v upozornění).</summary>
public sealed class ResendConfirmationModel(
    UserManager<ApplicationUser> userManager,
    AccountEmails accountEmails,
    ILogger<ResendConfirmationModel> logger) : PageModel
{
    public string? Message { get; private set; }

    public bool Failed { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.EmailConfirmed)
        {
            Message = "E-mail už je potvrzený.";
            return Page();
        }

        try
        {
            await accountEmails.SendConfirmationAsync(user, HttpContext, cancellationToken);
            Message = $"Poslali jsme nový odkaz na {user.Email}. Platí 2 hodiny.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Potvrzovací e-mail pro uživatele {UserId} se nepodařilo odeslat", user.Id);
            Failed = true;
            Message = "E-mail se teď nepodařilo odeslat. Zkuste to prosím za chvíli znovu.";
        }

        return Page();
    }
}
