using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Payments;
using MiniShop.Domain.Payments;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Infrastructure.Payments;

namespace MiniShop.Tests.Web;

/// <summary>
/// E-shop krok 8: webhook platební brány přes skutečné HTTP (podpis HMAC, 401, opakování) a stránka simulované brány
/// (zákazník vidí jen svou platbu). Platby se zakládají rovnou v payments.db – sága tu neběží.
/// </summary>
[Collection(WebCollection.Name)]
public sealed class PaymentPageTests(MiniShopWebFactory web)
{
    [Fact]
    public async Task Webhook_SpatnyPodpis_401_PlatbaBezeZmeny()
    {
        var payment = await CreatePaymentAsync(orderId: 9001, customerId: "nekdo");
        var (body, _) = Sign(new PaymentWebhookPayload(payment, PaymentWebhookPayload.Succeeded));

        var forged = await PostWebhookAsync(body, signature: new string('0', 64));
        var missing = await PostWebhookAsync(body, signature: null);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (forged.StatusCode, missing.StatusCode));
        Assert.Equal(PaymentStatus.Pending, await StatusAsync(payment));
    }

    [Fact]
    public async Task Webhook_SpravnyPodpis_200_OpakovanyTaky200AJednaZprava()
    {
        var payment = await CreatePaymentAsync(orderId: 9002, customerId: "nekdo");
        var (body, signature) = Sign(new PaymentWebhookPayload(payment, PaymentWebhookPayload.Succeeded));

        var first = await PostWebhookAsync(body, signature);
        var second = await PostWebhookAsync(body, signature);   // brána neviděla odpověď a poslala znovu

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment));
    }

    [Fact]
    public async Task Webhook_NeznamaPlatba_404_NecitelneTelo_400()
    {
        var (unknown, unknownSignature) = Sign(new PaymentWebhookPayload(Guid.NewGuid(), PaymentWebhookPayload.Succeeded));
        const string garbage = "tohle není JSON";
        var garbageSignature = WebhookSignature.Compute(MiniShopWebFactory.WebhookSecret, garbage);

        Assert.Equal(HttpStatusCode.NotFound, (await PostWebhookAsync(unknown, unknownSignature)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWebhookAsync(garbage, garbageSignature)).StatusCode);
    }

    [Fact]
    public async Task StrankaBrany_VlastniPlatbaSTlacitky_CiziNenajde()
    {
        const string email = "platba-1@example.com";
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var customer = await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
        var orderId = await web.CreateOrderForAsync(email);
        await CreatePaymentAsync(orderId, customerId: await UserIdAsync(email));
        await CreatePaymentAsync(orderId: 9004, customerId: "nekdo-jiny");

        var own = WebUtility.HtmlDecode(await customer.GetStringAsync($"/Pay/{orderId}"));
        var foreign = await customer.GetAsync("/Pay/9004");

        Assert.Contains("data-track=\"pay-confirm\"", own);
        Assert.Contains("Simulace platební brány", own);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task StrankaBrany_Zaplatit_PlatbaZaplacenaAPresmerujeNaObjednavku()
    {
        const string email = "platba-2@example.com";
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var customer = await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
        var orderId = await web.CreateOrderForAsync(email);
        var payment = await CreatePaymentAsync(orderId, customerId: await UserIdAsync(email));
        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(customer, $"/Pay/{orderId}");

        var response = await customer.PostAsync($"/Pay/{orderId}?handler=Pay", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal($"/Orders/{orderId}", response.Headers.Location?.OriginalString);
        Assert.Equal(PaymentStatus.Succeeded, await StatusAsync(payment));
    }

    [Fact]
    public async Task StrankaBrany_ZrusenaObjednavka_BezTlacitekAZaplatitNejde()
    {
        // E-shop krok 9: zákazník zrušil objednávku, která čekala na platbu – stránka brány už platit nedovolí.
        const string email = "platba-3@example.com";
        await web.CreateUserAsync(email, MiniShopWebFactory.UserPassword);
        var customer = await web.LoginAsync(email, MiniShopWebFactory.UserPassword);
        var orderId = await web.CreateOrderForAsync(email, cancelled: true);
        var payment = await CreatePaymentAsync(orderId, customerId: await UserIdAsync(email));

        var page = WebUtility.HtmlDecode(await customer.GetStringAsync($"/Pay/{orderId}"));
        Assert.Contains("Objednávka je zrušená", page);
        Assert.DoesNotContain("data-track=\"pay-confirm\"", page);

        var token = await MiniShopWebFactory.GetAntiforgeryTokenAsync(customer, $"/Pay/{orderId}");
        await customer.PostAsync($"/Pay/{orderId}?handler=Pay", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));   // ručně poslaný formulář

        Assert.Equal(PaymentStatus.Pending, await StatusAsync(payment));
    }

    private async Task<Guid> CreatePaymentAsync(int orderId, string customerId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payment = new Payment(orderId, customerId, 250m, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30));
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<PaymentStatus> StatusAsync(Guid paymentId)
    {
        await using var scope = web.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().GetAsync(paymentId))!.Status;
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = web.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    private (string Body, string Signature) Sign(PaymentWebhookPayload payload)
    {
        using var scope = web.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FakePaymentGatewaySimulator>().Sign(payload);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature)
    {
        // Brána nemá účet ani cookie – obyčejný klient bez přihlášení.
        var client = web.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (signature is not null)
        {
            request.Headers.Add(WebhookSignature.HeaderName, signature);
        }

        return await client.SendAsync(request);
    }
}
