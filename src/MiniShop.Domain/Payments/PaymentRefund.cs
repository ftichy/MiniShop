namespace MiniShop.Domain.Payments;

/// <summary>
/// E-shop krok 22: jedno vrácení peněz z platby. Vratka zboží po částech = víc vrácení k jedné platbě
/// (<see cref="ReturnNumber"/> 1, 2, …); celé vrácení (pozdní platba, zrušení, vratka celé objednávky) má číslo 0.
/// Unikátní platba + číslo – stejná vratka peníze podruhé nevrátí.
/// </summary>
public sealed class PaymentRefund
{
    /// <summary>Číslo „vratky“ u celého vrácení zbývající částky (<see cref="Payment.Refund"/>).</summary>
    public const int FullRefundNumber = 0;

    // Konstruktor pro EF Core.
    private PaymentRefund()
    {
    }

    internal PaymentRefund(int returnNumber, decimal amount, DateTime refundedAtUtc)
    {
        ReturnNumber = returnNumber;
        Amount = amount;
        RefundedAtUtc = refundedAtUtc;
    }

    public int Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public int ReturnNumber { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime RefundedAtUtc { get; private set; }
}
