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
            // Počkat na konec animace (třída "show" místo "collapsing") – klepnutí během ní Bootstrap ignoruje.
            await Expect(menu).ToHaveClassAsync(new Regex(@"(^|\s)show(\s|$)"));
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

    /// <summary>
    /// Vzhled krok D: aplikace na telefonu – spodní lišta, service worker se zaregistruje a bez signálu ukáže offline
    /// stránku (místo chyby prohlížeče); po návratu sítě „Zkusit znovu“ načte stránku normálně.
    /// </summary>
    [E2EFact]
    public async Task Mobil_Aplikace_SpodniListaAOfflineStranka()
    {
        Session? phone = null;
        await RunAsync(async () =>
        {
            phone = await NewSessionAsync("mobil-aplikace", mobile: true);
            var page = phone.Page;

            await page.GotoAsync("/Shop");
            await Expect(page.Locator("[data-track=tab-bar]")).ToBeVisibleAsync();
            await Expect(page.Locator("[data-track=tab-shop]")).ToHaveClassAsync(new Regex(@"(^|\s)active(\s|$)"));

            // Service worker běží jen v bezpečném kontextu – E2E jede na http://localhost (ten se za bezpečný považuje).
            var controlled = await page.EvaluateAsync<bool>(
                "async () => { await navigator.serviceWorker.ready; " +
                "if (!navigator.serviceWorker.controller) { await new Promise(r => navigator.serviceWorker.addEventListener('controllerchange', r, { once: true })); } " +
                "return true; }");
            Assert.True(controlled);

            await phone.Context.SetOfflineAsync(true);
            await page.GotoAsync("/Shop?offline-test=1");
            await Expect(page.Locator("[data-track=offline-page]")).ToBeVisibleAsync();

            await phone.Context.SetOfflineAsync(false);
            await page.Locator("[data-track=offline-retry]").ClickAsync();
            await Expect(page.Locator("[data-track=tab-bar]")).ToBeVisibleAsync();
        }, () => phone);

        if (phone is not null)
        {
            await phone.DisposeAsync();
        }
    }
}
