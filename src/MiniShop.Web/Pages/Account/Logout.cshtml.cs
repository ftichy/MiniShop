using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Pages.Account;

/// <summary>
/// Účty krok 1: odhlášení smaže přihlašovací cookie. Jen přes POST s antiforgery tokenem – odkaz nebo obrázek
/// z cizího webu (GET) uživatele odhlásit nesmí.
/// </summary>
public sealed class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Index");
    }
}
