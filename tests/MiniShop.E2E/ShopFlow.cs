using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MiniShop.E2E;

/// <summary>
/// Kroky nákupní cesty, ze kterých se skládají scénáře. Selektory jdou přes <c>data-track</c> a ID polí
/// (na nich stojí Journeys, takže se nemění) – texty jen tam, kde žádný z nich není.
/// Čekání na asynchronní část (sága, katalog) = opakované načtení stránky s limitem, žádné pevné spaní.
/// </summary>
public static class ShopFlow
{
    private static readonly MailpitClient Mailpit = new(E2EEnvironment.MailpitUrl);

    public static string NewPassword() => "E2e-heslo-" + Guid.NewGuid().ToString("N");

    public static string NewEmail() => $"e2e-{Guid.NewGuid():N}@example.test";

    /// <summary>Registrace → odkaz z e-mailu (Mailpit) → potvrzení. Zákazník zůstane přihlášený.</summary>
    public static async Task<string> RegisterAndConfirmAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("#Input_DisplayName").FillAsync("E2E Zákazník");
        await page.Locator("#Input_Email").FillAsync(email);
        await page.Locator("#Input_Password").FillAsync(password);
        await page.Locator("#Input_ConfirmPassword").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Zaregistrovat" }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Register"));

        // Odkaz míří na PublicBaseUrl aplikace; v testu ho otevřeme přes adresu, na které web opravdu běží.
        var link = await Mailpit.WaitForLinkAsync(email, "/Account/ConfirmEmail", E2EEnvironment.Patience);
        await page.GotoAsync(new Uri(link).PathAndQuery);
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("E-mail je potvrzený");
        return email;
    }

    public static async Task LoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("#Input_Email").FillAsync(email);
        await page.Locator("#Input_Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Přihlásit" }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login"));
    }

    /// <summary>Tajný klíč 2FA admina z E2E (nastaví se při prvním přihlášení; testy běží v jednom procesu).</summary>
    private static string? _adminTotpKey;

    /// <summary>
    /// Přihlášení admina s dvoufázovým ověřením. Admin z <c>create-admin</c> ho zatím nemá – po přihlášení heslem
    /// ho přes UI nastaví (klíč přečte ze stránky, první kód spočítá sám); další přihlášení zadá kód.
    /// Žádná zadní vrátka v aplikaci: stejné kroky udělá člověk s telefonem.
    /// </summary>
    public static async Task LoginAdminAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("#Input_Email").FillAsync(email);
        await page.Locator("#Input_Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Přihlásit" }).ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath != "/Account/Login");

        if (new Uri(page.Url).AbsolutePath == "/Account/LoginWith2fa")
        {
            await page.Locator("#Input_Code").FillAsync(Totp.Compute(_adminTotpKey
                ?? throw new InvalidOperationException("Admin už 2FA má, ale test jeho klíč nezná.")));
            await page.GetByRole(AriaRole.Button, new() { Name = "Ověřit" }).ClickAsync();
            await page.WaitForURLAsync(url => new Uri(url).AbsolutePath != "/Account/LoginWith2fa");
            return;
        }

        await page.GotoAsync("/Account/TwoFactor");
        var key = (await page.Locator("[data-track=two-factor-key]").InnerTextAsync()).Trim();
        await page.Locator("#code-enable").FillAsync(Totp.Compute(key));
        await page.GetByRole(AriaRole.Button, new() { Name = "Zapnout" }).ClickAsync();
        await Expect(page.Locator("[data-track=two-factor-recovery-codes]")).ToBeVisibleAsync();
        _adminTotpKey = key;
    }

    /// <summary>Katalog se po startu / založení produktu plní přes zprávy (~2–4 s) – obnovujeme výlohu, dokud tlačítko „Do košíku“ není.</summary>
    public static async Task WaitForProductsAsync(IPage page)
    {
        await ReloadUntilAsync(page, "/Shop", page.Locator("[data-track=shop-add-to-cart]").First, "produkty ve výloze");
    }

    /// <summary>Přidá první produkt z výlohy do košíku (výloha → POST /Cart?handler=Add → zpět do výlohy).</summary>
    public static async Task AddFirstProductToCartAsync(IPage page)
    {
        await WaitForProductsAsync(page);
        await page.Locator("[data-track=shop-add-to-cart]").First.ClickAsync();
        await page.WaitForURLAsync(new Regex("/Shop"));
    }

    /// <summary>Košík → pokladna (tlačítko podle velikosti obrazovky) → formulář → Objednat. Vrací číslo objednávky.</summary>
    public static async Task<int> CheckoutAsync(IPage page, bool mobile, string payment)
    {
        await page.GotoAsync("/Cart");
        await page.Locator(mobile ? "[data-track=cart-checkout-mobile]" : "[data-track=cart-checkout]").ClickAsync();
        await page.WaitForURLAsync(new Regex("/Checkout"));

        await FillCheckoutFormAsync(page, payment);
        await page.Locator("[data-track=checkout-submit]").ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/Orders/\d+"));
        return OrderIdFrom(page.Url);
    }

    public static async Task FillCheckoutFormAsync(IPage page, string payment)
    {
        await page.Locator("#shipping-Courier").CheckAsync();
        await page.Locator($"#payment-{payment}").CheckAsync();
        await page.Locator("#Input_Street").FillAsync("Testovací 1");
        await page.Locator("#Input_City").FillAsync("Praha");
        await page.Locator("#Input_PostalCode").FillAsync("11000");
    }

    public static int OrderIdFrom(string url) => int.Parse(Regex.Match(url, @"/Orders/(\d+)").Groups[1].Value);

    /// <summary>Na detailu objednávky počká (obnovováním), až stav bude <paramref name="statusText"/> (např. „Potvrzená“).</summary>
    public static async Task WaitForOrderStatusAsync(IPage page, int orderId, string statusText)
    {
        var status = page.Locator("[data-track=order-detail-status]").Filter(new() { HasText = statusText });
        await ReloadUntilAsync(page, $"/Orders/{orderId}", status, $"stav „{statusText}“ objednávky {orderId}");
    }

    /// <summary>Platba kartou na simulované bráně: detail (čeká na ságu) → „Zaplatit“ → /Pay → „Zaplatit“.</summary>
    public static async Task PayByCardAsync(IPage page, int orderId)
    {
        await ReloadUntilAsync(page, $"/Orders/{orderId}", page.Locator("[data-track=order-pay]"), "odkaz Zaplatit (sága rezervuje zboží)");
        await page.Locator("[data-track=order-pay]").ClickAsync();
        await page.WaitForURLAsync(new Regex("/Pay/"));
        await ReloadUntilAsync(page, page.Url, page.Locator("[data-track=pay-confirm]"), "tlačítko platby na bráně");
        await page.Locator("[data-track=pay-confirm]").ClickAsync();
        // Brána přesměruje na detail s hláškou (Pay/Index.cshtml.cs – StatusMessage).
        await page.WaitForURLAsync(new Regex($"/Orders/{orderId}"));
        await Expect(page.GetByText("Platba proběhla").First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Obnovuje stránku, dokud se prvek neobjeví – nejdéle <see cref="E2EEnvironment.Patience"/>.
    /// Chybí-li prvek i po limitu, spadne s popisem, na co se čekalo.
    /// </summary>
    public static async Task ReloadUntilAsync(IPage page, string url, ILocator target, string what)
    {
        var deadline = DateTime.UtcNow + E2EEnvironment.Patience;
        await page.GotoAsync(url);
        while (true)
        {
            if (await target.CountAsync() > 0)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Nedočkali jsme se: {what} (stránka {url}, {E2EEnvironment.Patience.TotalSeconds:0} s).");
            }

            await Task.Delay(1000);
            await page.ReloadAsync();
        }
    }
}
