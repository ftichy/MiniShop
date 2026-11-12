using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// SignInManager MiniShopu: admin zadává kód z aplikace při každém přihlášení. „Zapamatované zařízení“ (cookie,
/// kterou si dřív zvolil zákazník) se u admina neuznává – ani když se uživatel později stal adminem a cookie
/// v prohlížeči zůstala. Rozhoduje se při přihlášení podle role, takže na tom, kde cookie leží, nezáleží.
/// </summary>
public sealed class AccountSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> IsTwoFactorClientRememberedAsync(ApplicationUser user) =>
        TwoFactorHelper.MayRememberDevice(await UserManager.GetRolesAsync(user))
        && await base.IsTwoFactorClientRememberedAsync(user);
}
