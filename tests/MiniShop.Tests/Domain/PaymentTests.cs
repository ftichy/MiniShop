using MiniShop.Application.Payments;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Payments;
using MiniShop.Domain.Payments.Events;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 8: platba jako stavový automat, podpis webhooku a pravidla objednávky pro platbu kartou.</summary>
public sealed class PaymentTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Payment NewPayment() => new(42, "zakaznik-1", 250m, Now, Now.AddMinutes(30));

    [Fact]
    public void Zaplaceni_JednaUdalost_OpakovanyWebhookNicNezmeni()
    {
        var payment = NewPayment();

        Assert.True(payment.Succeed(Now));
        Assert.False(payment.Succeed(Now));   // brána poslala znovu

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.IsType<PaymentSucceeded>(Assert.Single(payment.DomainEvents));
    }

    [Fact]
    public void ZaplacenouNejdeZamitnout_VratitJdeJednou()
    {
        var payment = NewPayment();
        payment.Succeed(Now);

        Assert.Throws<DomainException>(() => payment.Fail("pozdě", Now));
        Assert.True(payment.Refund(Now));
        Assert.False(payment.Refund(Now));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void Vratit_NezaplacenouNejde()
    {
        Assert.Throws<DomainException>(() => NewPayment().Refund(Now));
    }

    // ---------- E-shop krok 22: vrácení po částech ----------

    [Fact]
    public void CastecneVraceni_KazdaVratkaJednou_PosledniCastJeCeleVraceni()
    {
        var payment = NewPayment();   // 250 Kč
        payment.Succeed(Now);

        var first = payment.RefundPart(1, 100m, Now);
        Assert.NotNull(first);
        Assert.Null(payment.RefundPart(1, 100m, Now));   // stejná vratka podruhé – nic
        Assert.Equal((100m, 150m, PaymentStatus.Succeeded), (payment.RefundedAmount, payment.RefundableAmount, payment.Status));

        Assert.Throws<DomainException>(() => payment.RefundPart(2, 150.01m, Now));   // víc, než zbývá
        Assert.Throws<DomainException>(() => payment.RefundPart(2, 0m, Now));

        payment.RefundPart(2, 150m, Now);
        Assert.Equal((250m, 0m, PaymentStatus.Refunded), (payment.RefundedAmount, payment.RefundableAmount, payment.Status));
        Assert.False(payment.Refund(Now));   // celé vrácení už nic nevrátí
        Assert.Equal(2, payment.DomainEvents.OfType<PaymentPartlyRefunded>().Count());
        Assert.Empty(payment.DomainEvents.OfType<PaymentRefunded>());   // V1 („vráceno celé“) jen z Refund
    }

    [Fact]
    public void CeleVraceniPoCastecnem_VratiJenZbytek()
    {
        var payment = NewPayment();
        payment.Succeed(Now);
        payment.RefundPart(1, 50m, Now);

        Assert.True(payment.Refund(Now));

        Assert.Equal(200m, payment.DomainEvents.OfType<PaymentRefunded>().Single().Amount);
        Assert.Equal(250m, payment.RefundedAmount);
        Assert.Throws<DomainException>(() => payment.RefundPart(2, 1m, Now));   // vrácená celá
    }

    [Fact]
    public void CastecneVraceni_JenZaplacene()
    {
        Assert.Throws<DomainException>(() => NewPayment().RefundPart(1, 10m, Now));
        var payment = NewPayment();
        payment.Succeed(Now);
        Assert.Throws<DomainException>(() => payment.RefundPart(0, 10m, Now));   // 0 = celé vrácení, ne vratka
    }

    [Fact]
    public void Podpis_ZmenaTelaHoRozbije()
    {
        const string secret = "tajemstvi-pro-test-podpisu-webhooku-0123";
        var body = """{"paymentId":"11111111-2222-3333-4444-555555555555","status":"succeeded"}""";
        var signature = WebhookSignature.Compute(secret, body);

        Assert.True(WebhookSignature.IsValid(secret, body, signature));
        Assert.True(WebhookSignature.IsValid(secret, body, signature.ToUpperInvariant()));
        Assert.False(WebhookSignature.IsValid(secret, body.Replace("succeeded", "failed"), signature));
        Assert.False(WebhookSignature.IsValid("jine-tajemstvi-pro-test-podpisu-webhooku", body, signature));
        Assert.False(WebhookSignature.IsValid(secret, body, null));
    }

    [Fact]
    public void Tajemstvi_KratkeNebyPustiloSluzbuSpustit()
    {
        Assert.Throws<InvalidOperationException>(() => new PaymentsSettings("kratke"));
    }

    [Fact]
    public void Objednavka_KartouSePotvrdiAzPoPlatbe_DobirkaHned()
    {
        var card = new Order("zakaznik-1", "Jan", "jan@example.com", Now);
        card.SetPayment(PaymentMethod.Card);
        Assert.Throws<DomainException>(() => card.Confirm(Now));   // bez platby ne
        card.AwaitPayment(Now);
        card.Confirm(Now);
        Assert.Equal(OrderStatus.Confirmed, card.Status);

        var cod = new Order("zakaznik-1", "Jan", "jan@example.com", Now);
        cod.SetPayment(PaymentMethod.CashOnDelivery);
        Assert.Throws<DomainException>(() => cod.AwaitPayment(Now));
        cod.Confirm(Now);
        Assert.Equal(OrderStatus.Confirmed, cod.Status);
    }

    [Fact]
    public void Objednavka_NezaplacenaJdeZamitnout()
    {
        var order = new Order("zakaznik-1", "Jan", "jan@example.com", Now);
        order.SetPayment(PaymentMethod.Card);
        order.AwaitPayment(Now);
        Assert.Throws<DomainException>(() => order.Anonymize());   // čeká na platbu = rozpracovaná, účet nejde smazat

        order.Reject("Platba nebyla dokončena včas.", Now);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        order.Anonymize();   // zamítnutá už rozpracovaná není
    }
}
