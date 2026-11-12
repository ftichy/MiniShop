using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>Scénář 3: nákupní cesta ve viewportu telefonu (390×844) – menu ☰, filtry v panelu, spodní lišta košíku.</summary>
public sealed class MobilTests : E2ETestBase
{
    [E2EFact]
    public async Task Mobil_VylohaKosikPokladna_FungujeVMenuFiltrechAListeKosiku()
    {
        Session? phone = null;
        await RunAsync(async () =>
        {
            phone = await NewSessionAsync("mobil", mobile: true);
            var page = phone.Page;

            await ShopFlow.RegisterAndConfirmAsync(page, ShopFlow.NewEmail(), ShopFlow.NewPassword());
            await ShopFlow.WaitForProductsAsync(page);

            // Menu ☰: na telefonu je sbalené a otevře se klepnutím.
            var menu = page.Locator("#main-nav");
            await Expect(menu).ToBeHiddenAsync();
            await page.Locator("button.navbar-toggler").ClickAsync();
            await Expect(menu).ToBeVisibleAsync();
            await page.Locator("button.navbar-toggler").ClickAsync();
            await Expect(menu).ToBeHiddenAsync();

            // Filtry jsou v panelu (offcanvas): zavřené, po klepnutí se otevřou, hledání je v nich.
            await Expect(page.Locator("[data-track=shop-search]")).ToBeHiddenAsync();
            await page.Locator("[data-track=shop-filters-open]").ClickAsync();
            await Expect(page.Locator("[data-track=shop-search]")).ToBeVisibleAsync();
            await page.Locator("[data-track=shop-search]").FillAsync("a");
            await page.Locator("[data-track=shop-submit]").ClickAsync();
            await page.WaitForURLAsync(new Regex("/Shop"));
            await ShopFlow.WaitForProductsAsync(page);

            // Košík a pokladna (spodní lišta „K pokladně“ – na telefonu je jiné tlačítko než na počítači).
            await page.Locator("[data-track=shop-add-to-cart]").First.ClickAsync();
            await page.GotoAsync("/Cart");
            await Expect(page.Locator("[data-track=cart-checkout-mobile]")).ToBeVisibleAsync();
            await page.Locator("[data-track=cart-checkout-mobile]").ClickAsync();
            await page.WaitForURLAsync(new Regex("/Checkout"));

            await ShopFlow.FillCheckoutFormAsync(page, "CashOnDelivery");
            await Expect(page.Locator("[data-track=checkout-submit]")).ToBeEnabledAsync();
        }, () => phone);

        if (phone is not null)
        {
            await phone.DisposeAsync();
        }
    }
}
