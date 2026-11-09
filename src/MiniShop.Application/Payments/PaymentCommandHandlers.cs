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
    IIntegrationMessageHandler<RefundPaymentV1>
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

        if (payment.Refund(timeProvider.GetUtcNow().UtcDateTime))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);   // → PaymentRefundedV1
        }
    }
}
