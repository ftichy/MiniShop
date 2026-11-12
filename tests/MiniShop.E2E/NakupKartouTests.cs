using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>Scénář 1: zákazník od registrace po potvrzenou objednávku zaplacenou kartou na simulované bráně.</summary>
public sealed class NakupKartouTests : E2ETestBase
{
    [E2EFact]
    public async Task Zakaznik_ZaregistrujeSeNakoupiKartouAObjednavkaSeNaPotvrdi()
    {
        Session? customer = null;
        await RunAsync(async () =>
        {
            customer = await NewSessionAsync("nakup-kartou");
            var page = customer.Page;

            await ShopFlow.RegisterAndConfirmAsync(page, ShopFlow.NewEmail(), ShopFlow.NewPassword());
            await ShopFlow.AddFirstProductToCartAsync(page);

            var orderId = await ShopFlow.CheckoutAsync(page, mobile: false, payment: "Card");

            // Sága rezervuje zboží (~4–8 s) → objednávka čeká na platbu → zákazník platí na bráně.
            await ShopFlow.PayByCardAsync(page, orderId);

            // Webhook brány → sága potvrdí objednávku.
            await ShopFlow.WaitForOrderStatusAsync(page, orderId, "Potvrzená");
            await Expect(page.Locator("[data-track=order-detail-history]")).ToContainTextAsync(new Regex("Potvrzen"));
        }, () => customer);

        if (customer is not null)
        {
            await customer.DisposeAsync();
        }
    }
}
