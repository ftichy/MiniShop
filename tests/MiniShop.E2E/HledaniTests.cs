using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>
/// Scénář 4 (e-shop krok 21): našeptávač hledání – návrh jde opravdu otevřít kliknutím (počítač, i dlouhý stisk)
/// a klepnutím (telefon). Chyba z produkce: nabídka se při blur skryla dřív, než klik doběhl, a odkaz se neotevřel.
/// </summary>
public sealed class HledaniTests : E2ETestBase
{
    [E2EFact]
    public async Task Naseptavac_KlikNaNavrh_OtevreProdukt()
    {
        Session? desktop = null;
        await RunAsync(async () =>
        {
            desktop = await NewSessionAsync("hledani");
            var page = desktop.Page;
            await ShopFlow.ReloadUntilAsync(page, "/Shop", page.Locator("[data-track=shop-product]").First, "produkty ve výloze");

            await page.Locator("[data-track=nav-search]").PressSequentiallyAsync("mys", new() { Delay = 50 });
            var suggestion = page.Locator(".search-suggest-list .search-suggest-product").First;
            await Expect(suggestion).ToBeVisibleAsync();

            // Dlouhý stisk (300 ms mezi stiskem a uvolněním) – dřív nabídka zmizela po 150 ms.
            await suggestion.ClickAsync(new() { Delay = 300 });
            await ExpectProductDetailAsync(page);
        }, () => desktop);

        if (desktop is not null)
        {
            await desktop.DisposeAsync();
        }
    }

    [E2EFact]
    public async Task Naseptavac_Mobil_KlepnutiNaNavrh_OtevreProdukt()
    {
        Session? phone = null;
        await RunAsync(async () =>
        {
            phone = await NewSessionAsync("hledani-mobil", mobile: true);
            var page = phone.Page;
            await ShopFlow.ReloadUntilAsync(page, "/Shop", page.Locator("[data-track=shop-product]").First, "produkty ve výloze");

            // Na telefonu je hledání ve výloze v panelu Filtry.
            await page.Locator("[data-track=shop-filters-open]").ClickAsync();
            // Počkat na konec animace – Bootstrap pak přesune fokus na panel a pole by fokus ztratilo (nabídka se zavře).
            await Expect(page.Locator("#shop-filters")).ToHaveClassAsync(new Regex(@"(^|\s)show(\s|$)"));
            await Expect(page.Locator("#shop-filters")).Not.ToHaveClassAsync(new Regex("showing"));
            await page.Locator("[data-track=shop-search]").PressSequentiallyAsync("mys", new() { Delay = 50 });
            var suggestion = page.Locator(".search-suggest-list .search-suggest-product").First;
            await Expect(suggestion).ToBeVisibleAsync();

            await suggestion.TapAsync();
            await ExpectProductDetailAsync(page);
        }, () => phone);

        if (phone is not null)
        {
            await phone.DisposeAsync();
        }
    }

    /// <summary>
    /// Otevřel se detail produktu. Kontroluje výsledek (adresa a tlačítko na stránce), ne průběh navigace:
    /// klik/klepnutí samo počká na začátek navigace a ta je často hotová dřív, než by WaitForURLAsync začal čekat –
    /// ten pak čekal na další událost „load“, která už nepřišla (nestabilní test, CI 16. a 17. 11. 2026).
    /// Expect opakuje kontrolu, dokud neplatí.
    /// </summary>
    private static async Task ExpectProductDetailAsync(Microsoft.Playwright.IPage page)
    {
        await Expect(page).ToHaveURLAsync(new Regex(@"/Shop/\d+$"));
        await Expect(page.Locator("[data-track=product-rating]")).ToBeVisibleAsync();
    }
}
