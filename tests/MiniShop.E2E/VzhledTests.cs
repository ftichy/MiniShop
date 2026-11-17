using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>
/// Vzhled krok E: tmavý režim. „Automaticky“ sleduje systém (prefers-color-scheme), ruční volba v patičce platí
/// i po obnovení stránky a na dalších stránkách (localStorage).
/// </summary>
public sealed class VzhledTests : E2ETestBase
{
    [E2EFact]
    public async Task TmavyRezim_PodleSystemu_RucniVolbaPrezijeObnoveni()
    {
        Session? desktop = null;
        await RunAsync(async () =>
        {
            desktop = await NewSessionAsync("vzhled");
            var page = desktop.Page;
            var html = page.Locator("html");

            // Automaticky: systém tmavý → tmavý MiniShop.
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
            await page.GotoAsync("/Shop");
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "dark");
            await Expect(page.Locator("[data-track=theme-auto]")).ToHaveAttributeAsync("aria-pressed", "true");

            // Ruční volba Světlý platí i po obnovení a na jiné stránce, přestože systém je tmavý.
            await page.Locator("[data-track=theme-light]").ClickAsync();
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
            await page.ReloadAsync();
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
            await page.GotoAsync("/Account/Login");
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");

            // Zpět na Automaticky → zase podle systému; přepnutí systému se projeví hned.
            await page.Locator("[data-track=theme-auto]").ClickAsync();
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "dark");
            await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
            await Expect(html).ToHaveAttributeAsync("data-bs-theme", "light");
        }, () => desktop);

        if (desktop is not null)
        {
            await desktop.DisposeAsync();
        }
    }
}
