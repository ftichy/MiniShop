using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>Scénář 2: admin objednávku zabalí, předá dopravci (číslo zásilky od simulátoru) a doručí; zákazník vidí „Doručená“.</summary>
public sealed class ExpediceTests : E2ETestBase
{
    [E2EAdminFact]
    public async Task Admin_ZabaliOdesleADorucí_ZakaznikVidiDorucenou()
    {
        Session? customer = null;
        Session? admin = null;
        await RunAsync(async () =>
        {
            customer = await NewSessionAsync("expedice-zakaznik");
            admin = await NewSessionAsync("expedice-admin");
            var shop = customer.Page;
            var back = admin.Page;

            // Zákazník: dobírka (platbu už pokrývá jiný scénář) → objednávka čeká na potvrzení ságou.
            await ShopFlow.RegisterAndConfirmAsync(shop, ShopFlow.NewEmail(), ShopFlow.NewPassword());
            await ShopFlow.AddFirstProductToCartAsync(shop);
            var orderId = await ShopFlow.CheckoutAsync(shop, mobile: false, payment: "CashOnDelivery");
            await ShopFlow.WaitForOrderStatusAsync(shop, orderId, "Potvrzená");

            // Admin: K zabalení → Předat dopravci (e-shop krok 26B: číslo přidělí simulovaný dopravce) → Na cestě → Doručeno.
            await ShopFlow.LoginAdminAsync(back, E2EEnvironment.AdminEmail!, E2EEnvironment.AdminPassword!);
            await ShopFlow.ShipAndDeliverAsync(back, orderId);

            // Zákazník po obnovení vidí „Doručená“ a číslo zásilky od dopravce.
            await ShopFlow.WaitForOrderStatusAsync(shop, orderId, "Doručená");
            await Expect(shop.Locator("[data-track=order-detail-tracking]")).ToContainTextAsync(new Regex(@"Simulovaný dopravce.*CS\d{10}"));
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
