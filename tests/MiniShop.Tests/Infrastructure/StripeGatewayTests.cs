using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Payments;
using MiniShop.Domain.Payments;
using MiniShop.Infrastructure.Payments.Stripe;
using MiniShop.Tests.TestDoubles;
using Stripe;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// E-shop krok 16: Stripe bez sítě – brána přes podvržený HttpClient (co přesně posíláme do API Stripu), webhook
/// s ručně podepsanou zprávou (stejný formát podpisu jako Stripe), platby v paměti. Skutečný Stripe se ověřuje ručně.
/// </summary>
public class StripeGatewayTests
{
    private const string WebhookSecret = "whsec_testovaci_tajemstvi_stripe";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly StripeSettings Settings = new("sk_test_123", WebhookSecret, "https://minishop.test/");

    // ---------- brána ----------

    [Fact]
    public async Task Zalozeni_CheckoutSessionVHalerich_SIdempotencyKey()
    {
        var api = new RecordingStripeApi("""{"id":"cs_test_1","object":"checkout.session","url":"https://checkout.stripe.com/c/pay/cs_test_1"}""");
        var payment = NewPayment(orderId: 42, amount: 199.90m);
        var requestId = Guid.NewGuid();

        var result = await Gateway(api).CreateAsync(payment, requestId);

        Assert.Equal(new GatewayPayment("cs_test_1", "https://checkout.stripe.com/c/pay/cs_test_1"), result);
        var request = Assert.Single(api.Requests);
        Assert.Equal((HttpMethod.Post, "/v1/checkout/sessions"), (request.Method, request.Path));
        Assert.Equal($"minishop-payment-{requestId:N}", request.IdempotencyKey);
        Assert.Equal("19990", request.Form["line_items[0][price_data][unit_amount]"]);
        Assert.Equal("czk", request.Form["line_items[0][price_data][currency]"]);
        Assert.Equal("42", request.Form["metadata[order_id]"]);
        Assert.Equal("https://minishop.test/Orders/42", request.Form["success_url"]);
        Assert.Equal(new DateTimeOffset(payment.ExpiresAtUtc + StripePaymentGateway.SessionExpiryExtension).ToUnixTimeSeconds(),
            long.Parse(request.Form["expires_at"]));
    }

    [Fact]
    public async Task OpakovanyPrikaz_StejnyPozadavek_StripeVratiTutezSession()
    {
        var api = new RecordingStripeApi("""{"id":"cs_test_1","object":"checkout.session","url":"https://checkout.stripe.com/x"}""");
        var requestId = Guid.NewGuid();

        // Opakované doručení RequestPaymentV1: handler založí novou instanci Payment (jiné naše ID), parametry ale stejné.
        await Gateway(api).CreateAsync(NewPayment(42, 100m), requestId);
        await Gateway(api).CreateAsync(NewPayment(42, 100m), requestId);

        Assert.Equal(api.Requests[0].IdempotencyKey, api.Requests[1].IdempotencyKey);
        Assert.Equal(api.Requests[0].Body, api.Requests[1].Body);   // jinak by Stripe odmítl „jiné parametry se stejným klíčem“
    }

    [Fact]
    public async Task Refund_PresPaymentIntentSession_SIdempotencyKey()
    {
        var api = new RecordingStripeApi(
            """{"id":"cs_test_1","object":"checkout.session","payment_intent":"pi_123"}""",
            """{"id":"re_1","object":"refund","status":"succeeded"}""");
        var payment = NewPayment(42, 100m);
        payment.AttachGateway("cs_test_1", "https://checkout.stripe.com/x");

        await Gateway(api).RefundAsync(payment);

        Assert.Equal((HttpMethod.Get, "/v1/checkout/sessions/cs_test_1"), (api.Requests[0].Method, api.Requests[0].Path));
        Assert.Equal((HttpMethod.Post, "/v1/refunds"), (api.Requests[1].Method, api.Requests[1].Path));
        Assert.Equal("pi_123", api.Requests[1].Form["payment_intent"]);
        Assert.Equal($"minishop-refund-{payment.Id:N}", api.Requests[1].IdempotencyKey);
    }

    [Theory]
    [InlineData(0.01, 1)]
    [InlineData(1250, 125000)]
    [InlineData(99.995, 10000)]
    public void Castka_VHalerich(decimal amount, long expected) =>
        Assert.Equal(expected, StripePaymentGateway.ToMinorUnits(amount));

    // ---------- webhook ----------

    [Fact]
    public async Task Webhook_Zaplaceno_PlatbaSucceeded()
    {
        var (processor, payments, unitOfWork) = Webhook();
        var payment = payments.Add(42, "cs_test_1");

        var result = await processor.ProcessAsync(signature: SignedEvent("checkout.session.completed", "cs_test_1", 42, out var body), body: body);

        Assert.Equal(PaymentWebhookResult.Accepted, result);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Webhook_Vyprselo_PlatbaFailed()
    {
        var (processor, payments, _) = Webhook();
        var payment = payments.Add(42, "cs_test_1");

        await processor.ProcessAsync(signature: SignedEvent("checkout.session.expired", "cs_test_1", 42, out var body), body: body);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public async Task Webhook_Opakovany_NicNezdvoji()
    {
        var (processor, payments, unitOfWork) = Webhook();
        payments.Add(42, "cs_test_1");
        var signature = SignedEvent("checkout.session.completed", "cs_test_1", 42, out var body);

        await processor.ProcessAsync(body, signature);
        var second = await processor.ProcessAsync(body, signature);

        Assert.Equal(PaymentWebhookResult.Accepted, second);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Webhook_CiziSession_JineProstredi_200BezEfektu()
    {
        // Testovací účet Stripu sdílí vývoj i produkce – objednávka 42 tady je jiná než objednávka 42 jinde.
        var (processor, payments, unitOfWork) = Webhook();
        var payment = payments.Add(42, "cs_test_nase");

        var result = await processor.ProcessAsync(signature: SignedEvent("checkout.session.completed", "cs_test_cizi", 42, out var body), body: body);

        Assert.Equal(PaymentWebhookResult.Accepted, result);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Webhook_SpatnyPodpis_StaraZprava_401()
    {
        var (processor, payments, _) = Webhook();
        var payment = payments.Add(42, "cs_test_1");
        var signature = SignedEvent("checkout.session.completed", "cs_test_1", 42, out var body);
        var old = SignedEvent("checkout.session.completed", "cs_test_1", 42, out var oldBody,
            signedAt: Now - StripeWebhookProcessor.Tolerance - TimeSpan.FromSeconds(1));

        Assert.Equal(PaymentWebhookResult.InvalidSignature, await processor.ProcessAsync(body.Replace("42", "43"), signature));
        Assert.Equal(PaymentWebhookResult.InvalidSignature, await processor.ProcessAsync(body, null));
        Assert.Equal(PaymentWebhookResult.InvalidSignature, await processor.ProcessAsync(oldBody, old));   // přehrání staré zprávy
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public async Task Webhook_JinaUdalostUctu_200BezEfektu()
    {
        var (processor, payments, unitOfWork) = Webhook();
        payments.Add(42, "cs_test_1");

        var result = await processor.ProcessAsync(signature: SignedEvent("customer.created", "cs_test_1", 42, out var body), body: body);

        Assert.Equal((PaymentWebhookResult.Accepted, 0), (result, unitOfWork.Saves));
    }

    // ---------- nastavení ----------

    [Fact]
    public void Nastaveni_ZivyKlicNeboChybejiciTajemstvi_WebSeNespusti()
    {
        Assert.Throws<InvalidOperationException>(() => new StripeSettings("sk_live_123", WebhookSecret, "https://minishop.test"));
        Assert.Throws<InvalidOperationException>(() => new StripeSettings("sk_test_123", null, "https://minishop.test"));
        Assert.Throws<InvalidOperationException>(() => new StripeSettings("sk_test_123", WebhookSecret, null));
        Assert.DoesNotContain("sk_test", Settings.ToString());   // klíč nikdy do logu
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Fake", false)]
    [InlineData("Stripe", true)]
    public void Nastaveni_VolbaBrany(string? gateway, bool stripe)
    {
        var configuration = Configuration(gateway);

        Assert.Equal(stripe, StripeSettings.FromConfiguration(configuration) is not null);
    }

    [Fact]
    public void Nastaveni_PreklepVeJmenuBrany_Chyba() =>
        Assert.Throws<InvalidOperationException>(() => StripeSettings.FromConfiguration(Configuration("Stirpe")));

    private static IConfiguration Configuration(string? gateway) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:Gateway"] = gateway,
            ["Payments:Stripe:SecretKey"] = "sk_test_123",
            ["Payments:Stripe:WebhookSecret"] = WebhookSecret,
            ["Notifications:PublicBaseUrl"] = "https://minishop.test"
        }).Build();

    private static StripePaymentGateway Gateway(RecordingStripeApi api) =>
        new(new StripeClient(apiKey: Settings.SecretKey, httpClient: new SystemNetHttpClient(new HttpClient(api), maxNetworkRetries: 0)),
            Settings);

    private static Payment NewPayment(int orderId, decimal amount) =>
        new(orderId, "zakaznik-1", amount, Now.UtcDateTime, Now.UtcDateTime.AddMinutes(30));

    private static (StripeWebhookProcessor, InMemoryPayments, CountingUnitOfWork) Webhook()
    {
        var payments = new InMemoryPayments();
        var unitOfWork = new CountingUnitOfWork();
        var time = new FixedTimeProvider(Now);
        var results = new PaymentWebhookProcessor(new PaymentsSettings(new string('x', 32)), payments, unitOfWork, time);
        return (new StripeWebhookProcessor(Settings, payments, results, time, NullLogger<StripeWebhookProcessor>.Instance),
            payments, unitOfWork);
    }

    /// <summary>Událost Stripu podepsaná jako od Stripu: <c>t=čas,v1=HMAC-SHA256("čas.tělo")</c>.</summary>
    private static string SignedEvent(string type, string sessionId, int orderId, out string body, DateTimeOffset? signedAt = null)
    {
        var timestamp = (signedAt ?? Now).ToUnixTimeSeconds();
        body = $$"""
            {"id":"evt_1","object":"event","api_version":"2026-01-01","created":{{timestamp}},"type":"{{type}}",
             "data":{"object":{"id":"{{sessionId}}","object":"checkout.session","payment_status":"paid",
             "metadata":{"order_id":"{{orderId}}"} } } }
            """;
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    /// <summary>„API Stripu“ – zapamatuje si požadavky a vrací připravené odpovědi popořadě.</summary>
    private sealed class RecordingStripeApi(params string[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null, body));
            var json = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? IdempotencyKey, string Body)
    {
        public IReadOnlyDictionary<string, string> Form => Body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair.ElementAtOrDefault(1) ?? string.Empty));
    }

    private sealed class InMemoryPayments : IPaymentRepository
    {
        private readonly List<Payment> _payments = [];

        public Payment Add(int orderId, string sessionId)
        {
            var payment = NewPayment(orderId, 100m);
            payment.AttachGateway(sessionId, $"https://checkout.stripe.com/c/pay/{sessionId}");
            _payments.Add(payment);
            return payment;
        }

        public Task<Payment?> GetAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.Id == paymentId));

        public Task<Payment?> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.OrderId == orderId));

        public void Add(Payment payment) => _payments.Add(payment);
    }

    private sealed class CountingUnitOfWork : IPaymentsUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(++Saves);
    }
}
