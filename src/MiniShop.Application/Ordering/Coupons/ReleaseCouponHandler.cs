using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.Coupons;

/// <summary>
/// E-shop krok 13: kompenzace – objednávka s kuponem neprošla (sklad nebo platba ji zamítly) nebo ji zákazník zrušil,
/// takže se použití kuponu vrací. Reaguje na doménové události uvnitř Orderingu (outbox), ne v pokladně.
/// </summary>
/// <remarks>
/// Obaluje ho <c>IdempotentDomainEventHandler</c> (inbox) – stejná událost nevrátí použití dvakrát. Souběh s pokladnou,
/// která zrovna kupon používá (verze kuponu), skončí výjimkou a outbox událost doručí znovu nad čerstvým stavem.
/// Vrácení zboží (e-shop krok 12) použití nevrací – nákup proběhl.
/// </remarks>
public sealed class ReleaseCouponHandler(ICouponRepository coupons, IOrderingUnitOfWork unitOfWork) :
    IDomainEventHandler<OrderRejected>,
    IDomainEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderRejected domainEvent, CancellationToken cancellationToken) =>
        ReleaseAsync(domainEvent.Order.CouponId, cancellationToken);

    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        ReleaseAsync(domainEvent.Order.CouponId, cancellationToken);

    private async Task ReleaseAsync(int? couponId, CancellationToken cancellationToken)
    {
        if (couponId is null || await coupons.GetByIdAsync(couponId.Value, cancellationToken) is not { } coupon)
        {
            return;
        }

        coupon.Release();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
