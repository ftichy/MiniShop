using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Payments.Events;

namespace MiniShop.Domain.Payments;

/// <summary>
/// E-shop krok 8: platba jedné objednávky kartou přes platební bránu. Stavový automat:
/// <code>
/// Pending ──Succeed──► Succeeded ──Refund──► Refunded
///    │                   │  ▲
///    │                   └──┘ RefundPart (e-shop krok 22: vratka po částech; poslední část → Refunded)
///    └────Fail─────► Failed
/// </code>
/// </summary>
/// <remarks>
/// Brány posílají stejný výsledek i víckrát (webhook bez odpovědi zopakují). Proto <see cref="Succeed"/> a <see cref="Fail"/>
/// na stejný výsledek podruhé jen vrátí false – nic se nezmění a žádná další událost nevznikne.
/// Payments objednávky nezná – jen číslo objednávky, částku a zákazníka z příkazu RequestPaymentV1.
/// </remarks>
public class Payment : Entity
{
    // Konstruktor pro EF Core.
    private Payment()
    {
    }

    public Payment(int orderId, string customerId, decimal amount, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Platba potřebuje číslo objednávky.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Částka platby musí být kladná.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainException("Platba musí mít termín v budoucnosti.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = PaymentStatus.Pending;
    }

    /// <summary>Naše ID platby – posílá se bráně a brána ho vrací ve webhooku.</summary>
    public Guid Id { get; private set; }

    /// <summary>Jedna objednávka = jedna platba (unikátní).</summary>
    public int OrderId { get; private set; }

    /// <summary>ID účtu zákazníka – stránku brány smí otevřít jen on.</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Do kdy jde zaplatit (termín ságy). Pozdní platbu brána (simulace) nepustí – a kdyby přišla, sága ji vrátí.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>ID platby u brány (vrací ho brána při založení).</summary>
    public string? ExternalId { get; private set; }

    /// <summary>Kam zákazníka poslat zaplatit (stránka brány).</summary>
    public string? CheckoutUrl { get; private set; }

    public string? FailureReason { get; private set; }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>Brána platbu založila – zapamatovat si její ID a stránku, kam poslat zákazníka.</summary>
    public void AttachGateway(string externalId, string checkoutUrl)
    {
        ExternalId = externalId;
        CheckoutUrl = checkoutUrl;
    }

    /// <summary>Webhook „zaplaceno“. Podruhé (opakovaný webhook) false – nic se nestane.</summary>
    public bool Succeed(DateTime nowUtc)
    {
        if (Status == PaymentStatus.Succeeded)
        {
            return false;
        }

        EnsurePending("Zaplatit lze jen čekající platbu.");
        Status = PaymentStatus.Succeeded;
        CompletedAtUtc = nowUtc;
        Raise(new PaymentSucceeded(this, nowUtc));
        return true;
    }

    /// <summary>Webhook „zamítnuto“. Podruhé false.</summary>
    public bool Fail(string reason, DateTime nowUtc)
    {
        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        EnsurePending("Zamítnout lze jen čekající platbu.");
        Status = PaymentStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Platba byla zamítnuta." : reason.Trim();
        CompletedAtUtc = nowUtc;
        Raise(new PaymentFailed(this, nowUtc));
        return true;
    }

    private readonly List<PaymentRefund> _refunds = [];

    /// <summary>E-shop krok 22: vrácení peněz (po částech za vratky, nebo jedno celé).</summary>
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// E-shop krok 22: kolik je vráceno celkem. Platby vrácené před krokem 22 nemají řádky vrácení – vrácená je celá částka.
    /// </summary>
    public decimal RefundedAmount => Status == PaymentStatus.Refunded && _refunds.Count == 0 ? Amount : _refunds.Sum(r => r.Amount);

    /// <summary>E-shop krok 22: kolik ještě jde vrátit.</summary>
    public decimal RefundableAmount => Status is PaymentStatus.Succeeded or PaymentStatus.Refunded ? Amount - RefundedAmount : 0m;

    /// <summary>
    /// E-shop krok 22: pojistka proti souběhu dvou vratek téže platby (součet nesmí přesáhnout zaplacené) – concurrency token.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Vrátit peníze celé (kompenzace pozdní platby, zrušení, vratka celé objednávky) – po částečných vráceních zbytek. Podruhé false.</summary>
    public bool Refund(DateTime nowUtc)
    {
        if (Status == PaymentStatus.Refunded)
        {
            return false;
        }

        if (Status != PaymentStatus.Succeeded)
        {
            throw new DomainException("Vrátit lze jen zaplacenou platbu.");
        }

        var amount = RefundableAmount;
        _refunds.Add(new PaymentRefund(PaymentRefund.FullRefundNumber, amount, nowUtc));
        Status = PaymentStatus.Refunded;
        Version++;
        Raise(new PaymentRefunded(this, amount, nowUtc));
        return true;
    }

    /// <summary>
    /// E-shop krok 22: vrátit část peněz za vratku zboží č. <paramref name="returnNumber"/>. Stejná vratka podruhé = null
    /// (nic se nestane). Když se vrácením dostane na celou částku, platba je <see cref="PaymentStatus.Refunded"/>.
    /// </summary>
    /// <returns>Nové vrácení (pro bránu), nebo null, když tahle vratka už peníze dostala.</returns>
    public PaymentRefund? RefundPart(int returnNumber, decimal amount, DateTime nowUtc)
    {
        if (returnNumber <= 0)
        {
            throw new DomainException("Částečné vrácení potřebuje číslo vratky.");
        }

        if (_refunds.Any(r => r.ReturnNumber == returnNumber))
        {
            return null;
        }

        if (Status != PaymentStatus.Succeeded)
        {
            throw new DomainException(Status == PaymentStatus.Refunded
                ? "Platba už je vrácená celá."
                : "Vrátit lze jen zaplacenou platbu.");
        }

        if (amount <= 0 || amount > RefundableAmount)
        {
            throw new DomainException($"Vrátit lze 0,01 až {RefundableAmount:0.00} Kč (zaplaceno {Amount:0.00} Kč, vráceno {RefundedAmount:0.00} Kč).");
        }

        var refund = new PaymentRefund(returnNumber, amount, nowUtc);
        _refunds.Add(refund);
        if (RefundableAmount == 0)
        {
            Status = PaymentStatus.Refunded;   // vratkami se vrátilo všechno
        }

        Version++;
        Raise(new PaymentPartlyRefunded(this, refund, nowUtc));
        return refund;
    }

    private void EnsurePending(string message)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainException(message);
        }
    }
}

/// <summary>E-shop krok 8: stav platby. Ukládá se jako text – nové hodnoty jen na konec.</summary>
public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded
}
