using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>
/// Scénář 4 (e-shop krok 22): vratka po částech – zákazník koupí 2 kusy na dobírku, po doručení vrátí 1 kus,
/// admin zásilku převezme a zákazník vidí vyřízenou vratku (objednávka zůstává doručená).
/// </summary>
public sealed class VratkaTests : E2ETestBase
{
    [E2EAdminFact]
    public async Task Zakaznik_VratiJedenKus_AdminPrevezme_VratkaVyrizena()
    {
        Session? customer = null;
        Session? admin = null;
        await RunAsync(async () =>
        {
            customer = await NewSessionAsync("vratka-zakaznik");
            admin = await NewSessionAsync("vratka-admin");
            var shop = customer.Page;
            var back = admin.Page;

            await ShopFlow.RegisterAndConfirmAsync(shop, ShopFlow.NewEmail(), ShopFlow.NewPassword());
            await ShopFlow.AddFirstProductToCartAsync(shop);
            await ShopFlow.AddFirstProductToCartAsync(shop);   // 2 kusy téhož produktu
            var orderId = await ShopFlow.CheckoutAsync(shop, mobile: false, payment: "CashOnDelivery");
            await ShopFlow.WaitForOrderStatusAsync(shop, orderId, "Potvrzená");

            await ShopFlow.LoginAdminAsync(back, E2EEnvironment.AdminEmail!, E2EEnvironment.AdminPassword!);
            await ShopFlow.ShipAndDeliverAsync(back, orderId);
            await ShopFlow.WaitForOrderStatusAsync(shop, orderId, "Doručená");

            // Zákazník: Vrátit zboží → 1 ze 2 kusů → důvod → odeslat (formulář posílá quantities[ID produktu]).
            await shop.Locator("[data-track=order-return] summary").ClickAsync();
            var quantity = shop.Locator("[data-track=order-return-quantity]").First;
            await Expect(quantity).ToHaveValueAsync("2");
            await quantity.FillAsync("1");
            await shop.Locator("[data-track=order-return-reason]").FillAsync("Jeden kus navíc");
            await shop.Locator("[data-track=order-return-submit]").ClickAsync();
            await Expect(shop.GetByText("Vratku 1 jsme přijali").First).ToBeVisibleAsync();
            await Expect(shop.Locator("[data-track=order-detail-status]")).ToContainTextAsync("Doručená");

            // Admin: Vratky → Přijato v pořádku (vratka 1).
            var row = back.Locator(".list-group-item")
                .Filter(new() { Has = back.GetByRole(AriaRole.Link, new() { Name = $"č. {orderId}", Exact = true }) });
            await ShopFlow.ReloadUntilAsync(back, "/Admin/Returns", row.Locator("[data-track=return-number]"), $"vratka objednávky {orderId}");
            await row.Locator("[data-track=return-accept]").ClickAsync();

            // Zákazník: vratka vyřízená (sága: sklad naskladní 1 kus, dobírka = peníze převodem), objednávka doručená.
            var returns = shop.Locator("[data-track=order-returns]").Filter(new() { HasText = "vyřízená" });
            await ShopFlow.ReloadUntilAsync(shop, $"/Orders/{orderId}", returns, $"vyřízená vratka objednávky {orderId}");
            await Expect(shop.Locator("[data-track=order-detail-status]")).ToContainTextAsync("Doručená");
        }, () => customer, () => admin);

        if (customer is not null)
        {
            await customer.DisposeAsync();
        }

        if (admin is not null)
        {
            await admin.DisposeAsync();
        }
    }
}
