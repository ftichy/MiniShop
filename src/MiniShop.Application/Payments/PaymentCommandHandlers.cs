using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Payments;
using MiniShop.Domain.Payments;

namespace MiniShop.Application.Payments;

/// <summary>
/// E-shop krok 8: modul Payments – účastník ságy objednávky. Příkazy přijímá přes svůj inbox (payments.db), odpovídá
/// událostmi přes svůj outbox. Objednávky nezná, jen to, co přijde v příkazu.
/// </summary>
public sealed class PaymentCommandHandlers(
    IPaymentRepository payments,
    IPaymentGateway gateway,
    IPaymentsUnitOfWork unitOfWork,
    TimeProvider timeProvider) :
    IIntegrationMessageHandler<RequestPaymentV1>,
    IIntegrationMessageHandler<RefundPaymentV1>,
    IIntegrationMessageHandler<RefundPaymentV2>
{
    /// <summary>Založit platbu a u brány stránku k zaplacení. Výsledek přijde později webhookem.</summary>
    public async Task HandleAsync(RequestPaymentV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        if (await payments.GetByOrderIdAsync(command.OrderId, cancellationToken) is not null)
        {
            return;   // platba už existuje (jedna na objednávku)
        }

        var payment = new Payment(command.OrderId, command.CustomerId, command.Amount,
            timeProvider.GetUtcNow().UtcDateTime, command.ExpiresAtUtc);
        // ID příkazu = idempotency key u brány: opakované doručení téhož příkazu nezaloží u brány druhou platbu.
        var atGateway = await gateway.CreateAsync(payment, command.CommandId, cancellationToken);
        payment.AttachGateway(atGateway.ExternalId, atGateway.CheckoutUrl);

        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TraceTags.Tag("minishop.payment.result", "requested");   // na trasu: platba založená, čeká na zákazníka
        TraceTags.Tag("minishop.payment.gateway", gateway.GetType().Name);   // FakePaymentGateway / StripePaymentGateway
        TraceTags.Tag("minishop.payment.amount", (double)command.Amount);
    }

    /// <summary>
    /// Kompenzace pozdní platby nebo zrušené zaplacené objednávky (e-shop krok 9) – vrátit peníze
    /// (u skutečné brány API „refund“ – e-shop krok 16; simulace jen změní stav).
    /// </summary>
    /// <remarks>
    /// Nejdřív brána, pak náš stav: když brána selže, výjimka → consumer zkusí znovu. Když spadne uložení po úspěšném
    /// refundu, opakování zavolá bránu znovu – ta díky idempotency key peníze podruhé nevrátí.
    /// </remarks>
    public async Task HandleAsync(RefundPaymentV1 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        var payment = await payments.GetByOrderIdAsync(command.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Platba objednávky {command.OrderId} neexistuje.");

        if (payment.Status == PaymentStatus.Succeeded)
        {
            await gateway.RefundAsync(payment, cancellationToken);
        }

        var refundable = payment.RefundableAmount;
        if (payment.Refund(timeProvider.GetUtcNow().UtcDateTime))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);   // → PaymentRefundedV1
            TraceTags.Tag("minishop.payment.result", "refunded");
            TraceTags.Tag("minishop.payment.amount", (double)refundable);
        }
    }

    /// <summary>
    /// E-shop krok 22: vratka zboží po částech – vrátit jen částku za vrácené položky. Stejná vratka podruhé nic neudělá.
    /// </summary>
    /// <remarks>
    /// Pořadí: nejdřív pravidla platby (<see cref="Payment.RefundPart"/> – součet nesmí přesáhnout zaplacené), pak brána,
    /// pak uložení. Když brána selže, nic se neuloží a consumer to zkusí znovu; pád po bráně zopakuje volání se stejným
    /// klíčem idempotence (platba + číslo vratky), takže peníze neodejdou dvakrát. Souběh dvou vratek téže platby
    /// zastaví <c>Payment.Version</c> (concurrency token) – druhá se zopakuje nad aktuálním součtem.
    /// </remarks>
    public async Task HandleAsync(RefundPaymentV2 command, CancellationToken cancellationToken)
    {
        TraceTags.TagOrder(command.OrderId);
        TraceTags.Tag("minishop.return.number", command.ReturnNumber);
        var payment = await payments.GetByOrderIdAsync(command.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Platba objednávky {command.OrderId} neexistuje.");

        var refund = payment.RefundPart(command.ReturnNumber, command.Amount, timeProvider.GetUtcNow().UtcDateTime);
        if (refund is null)
        {
            return;   // tahle vratka už peníze dostala (odpověď PaymentRefundedV2 odešla s jejím uložením)
        }

        await gateway.RefundPartAsync(payment, refund, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);   // → PaymentRefundedV2
        TraceTags.Tag("minishop.payment.result", "partly_refunded");
        TraceTags.Tag("minishop.payment.amount", (double)refund.Amount);
    }
}
