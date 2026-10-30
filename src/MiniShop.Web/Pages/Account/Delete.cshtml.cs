using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Exceptions;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Security;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 9: uživatel si smaže účet (GDPR – právo být zapomenut). Nejdřív znovu zadá heslo (kdo jen našel
/// otevřený prohlížeč, účet nesmaže), pak Web anonymizuje jeho objednávky (Ordering) a smaže účet (Accounts).
/// </summary>
/// <remarks>
/// Moduly se neznají – spojí je až tahle stránka. Pořadí: objednávky, pak účet (viz <see cref="AccountDeletion"/>).
/// </remarks>
[EnableRateLimiting(AccountRateLimiting.PolicyName)]   // ověřuje heslo – stejný limit jako přihlášení
public sealed class DeleteModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AccountDeletion accountDeletion,
    IOrderService orderService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Pro smazání účtu zadejte heslo.")]
    [DataType(DataType.Password)]
    [Display(Name = "Heslo")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Proč účet smazat nejde (admin) – formulář se pak neukáže.</summary>
    public string? CannotDeleteReason { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CannotDeleteReason = await accountDeletion.CanDeleteAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        CannotDeleteReason = await accountDeletion.CanDeleteAsync(user);
        if (CannotDeleteReason is not null || !ModelState.IsValid)
        {
            return Page();
        }

        // Heslo se počítá do zámku účtu (krok 8) – hádat ho tu nejde o nic lépe než při přihlášení.
        var check = await signInManager.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(Password), "Heslo nesouhlasí.");
            return Page();
        }

        try
        {
            await orderService.AnonymizeCustomerAsync(user.Id, cancellationToken);
        }
        catch (DomainException ex)
        {
            // Nevyřízené objednávky – nic se nezměnilo.
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var result = await accountDeletion.DeleteAsync(user, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await signInManager.SignOutAsync();
        StatusMessage = "Účet je smazaný. Objednávky zůstaly bez vašeho jména a e-mailu. Děkujeme, že jste nakupovali v MiniShopu.";
        return RedirectToPage("/Index");
    }
}
