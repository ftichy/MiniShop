using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>Scénář 2: admin objednávku zabalí, odešle (dopravce + číslo zásilky) a doručí; zákazník vidí „Doručená“.</summary>
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

            // Admin: K zabalení → K odeslání → Na cestě.
            await ShopFlow.LoginAdminAsync(back, E2EEnvironment.AdminEmail!, E2EEnvironment.AdminPassword!);
            await back.GotoAsync("/Admin/Fulfilment");
            await back.Locator($"input[name=ids][value='{orderId}']").CheckAsync();
            await back.Locator("[data-track=fulfilment-start-packing]").ClickAsync();

            await back.Locator($"#carrier-{orderId}").FillAsync("PPL");
            await back.Locator($"#tracking-{orderId}").FillAsync($"E2E{orderId}");
            await back.Locator($"form:has(#carrier-{orderId}) [data-track=fulfilment-ship]").ClickAsync();

            var inTransit = back.Locator(".list-group-item").Filter(new() { Has = back.GetByRole(AriaRole.Link, new() { Name = $"č. {orderId}", Exact = true }) })
                .Locator("[data-track=fulfilment-deliver]");
            await inTransit.ClickAsync();

            // Zákazník po obnovení vidí „Doručená“.
            await ShopFlow.WaitForOrderStatusAsync(shop, orderId, "Doručená");
            await Expect(shop.Locator("[data-track=order-detail-tracking]")).ToContainTextAsync($"E2E{orderId}");
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
