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
