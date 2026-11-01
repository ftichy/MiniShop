using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using MiniShop.Application.Abstractions;
using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.Accounts;

/// <summary>
/// Účty krok 5: e-maily účtu – potvrzení adresy a zapomenuté heslo. Každý nese odkaz s jednorázovým tokenem.
/// </summary>
/// <remarks>
/// Adresa odkazu se skládá z aktuálního požadavku (v produkci https://minishop-tichy.duckdns.org – Caddy předá
/// správné hlavičky). Accounts nemá outbox, e-mail se posílá hned během požadavku; chybu řeší volající stránka.
/// </remarks>
public sealed class AccountEmails(UserManager<ApplicationUser> userManager, IEmailSender emailSender, LinkGenerator links)
{
    public async Task SendConfirmationAsync(ApplicationUser user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var url = PageUrl(httpContext, "/Account/ConfirmEmail", new { userId = user.Id, code = EncodeToken(token) });

        await SendAsync(user, "potvrďte e-mail",
            "pro potvrzení e-mailové adresy otevřete odkaz:",
            url, "Pokud jste se v MiniShopu neregistrovali, e-mail ignorujte.", cancellationToken);
    }

    public async Task SendPasswordResetAsync(ApplicationUser user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var url = PageUrl(httpContext, "/Account/ResetPassword", new { email = user.Email, code = EncodeToken(token) });

        await SendAsync(user, "nové heslo",
            "někdo (nejspíš vy) požádal o nové heslo. Nastavíte ho na odkazu:",
            url, "Pokud jste o nové heslo nežádali, e-mail ignorujte – vaše heslo zůstává beze změny.", cancellationToken);
    }

    /// <summary>Token obsahuje znaky jako + a / – do adresy se kóduje bezpečně (Base64Url).</summary>
    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>Zpět na token; poškozený odkaz = null.</summary>
    public static string? DecodeToken(string? code)
    {
        try
        {
            return string.IsNullOrEmpty(code) ? null : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string PageUrl(HttpContext httpContext, string page, object values) =>
        links.GetUriByPage(httpContext, page, handler: null, values)
        ?? throw new InvalidOperationException($"Nepodařilo se sestavit adresu stránky {page}.");

    private Task SendAsync(ApplicationUser user, string subject, string intro, string url, string footer,
        CancellationToken cancellationToken)
    {
        var name = user.DisplayName;
        var text = $"Dobrý den, {name},\n\n{intro}\n{url}\n\nOdkaz platí 2 hodiny. {footer}\n\nMiniShop";
        var html = $"<p>Dobrý den, {WebUtility.HtmlEncode(name)},</p>" +
                   $"<p>{WebUtility.HtmlEncode(intro)}</p>" +
                   $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(url)}</a></p>" +
                   $"<p>Odkaz platí 2 hodiny. {WebUtility.HtmlEncode(footer)}</p><p>MiniShop</p>";

        return emailSender.SendAsync(new EmailMessage(user.Email!, $"MiniShop: {subject}", text, html), cancellationToken);
    }
}
