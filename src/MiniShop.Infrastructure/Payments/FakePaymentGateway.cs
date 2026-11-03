using System.Text.Json;
using MiniShop.Application.Payments;
using MiniShop.Domain.Payments;

namespace MiniShop.Infrastructure.Payments;

/// <summary>
/// E-shop krok 8: simulovaná platební brána. „Založí“ platbu (vymyslí ID u brány) a pošle zákazníka na stránku
/// /Pay/{objednávka}, která vypadá jako stránka banky. Skutečná brána by tu volala své API.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<GatewayPayment> CreateAsync(Payment payment, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayPayment($"FAKE-{payment.Id:N}"[..17], $"/Pay/{payment.OrderId}"));
}

/// <summary>
/// E-shop krok 8: „druhá strana“ simulované brány – zákazník na její stránce klikl Zaplatit / Zamítnout a brána nám
/// pošle výsledek. Vyrobí webhook úplně stejně jako skutečná brána (JSON + HMAC podpis sdíleným tajemstvím) a předá ho
/// stejnému zpracování jako HTTP endpoint /api/payments/webhook – jen bez cesty po síti.
/// </summary>
public sealed class FakePaymentGatewaySimulator(PaymentsSettings settings, PaymentWebhookProcessor processor)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<PaymentWebhookResult> PayAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        SendAsync(new PaymentWebhookPayload(paymentId, PaymentWebhookPayload.Succeeded), cancellationToken);

    public Task<PaymentWebhookResult> DeclineAsync(Guid paymentId, CancellationToken cancellationToken = default) =>
        SendAsync(new PaymentWebhookPayload(paymentId, PaymentWebhookPayload.Failed, "Platbu jste zamítli na stránce banky."),
            cancellationToken);

    /// <summary>Tělo a podpis webhooku – stejné, jaké by poslala brána (testy jím volají i HTTP endpoint).</summary>
    public (string Body, string Signature) Sign(PaymentWebhookPayload payload)
    {
        var body = JsonSerializer.Serialize(payload, Json);
        return (body, WebhookSignature.Compute(settings.WebhookSecret, body));
    }

    private Task<PaymentWebhookResult> SendAsync(PaymentWebhookPayload payload, CancellationToken cancellationToken)
    {
        var (body, signature) = Sign(payload);
        return processor.ProcessAsync(body, signature, cancellationToken);
    }
}
