using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Saga;

/// <summary>
/// E-shop krok 12: sága vrácení zboží – druhý dlouhý proces vedle <see cref="OrderSaga"/>. „Deník“ orchestrátoru
/// vratky: zákazník posílá zboží (dny), admin ho převezme, sklad naskladní, Payments vrátí peníze.
/// Stavový automat – každá metoda = jedna věc, která se může stát; vrací akci pro orchestrátor.
/// </summary>
/// <remarks>
/// <code>
/// AwaitingGoods ──GoodsReceived (admin)──► Restocking ──GoodsRestocked──► Refunding ──PaymentRefunded──► Completed
///      │                                                     └─ (dobírka – peníze vrací admin ručně) ──────► Completed
///      ├─Reject (admin, zboží poškozené)──► Rejected
///      └─timeout (zboží nedorazilo včas)──► Expired
/// </code>
/// Uprostřed je krok člověka (admin převezme zásilku) – sága na něj čeká stejně jako na zprávu, jen s delším termínem.
/// Zpráva, která do stavu nepatří (duplicita, zpoždění), vrátí <see cref="ReturnSagaAction.None"/>.
/// </remarks>
public sealed class ReturnSaga
{
    public const string ExpiredReason = "Vrácené zboží nedorazilo včas.";

    // Konstruktor pro EF Core.
    private ReturnSaga()
    {
    }

    /// <summary>ID objednávky = ID ságy (každou objednávku jde vrátit nejvýš jednou).</summary>
    public int OrderId { get; private set; }

    public ReturnSagaState State { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Do kdy musí zboží dorazit; pak vrácení vyprší.</summary>
    public DateTime GoodsDeadlineUtc { get; private set; }

    /// <summary>Placeno kartou – po naskladnění vrátit peníze přes Payments. Dobírku vrací admin ručně.</summary>
    public bool RequiresRefund { get; private set; }

    /// <summary>Proč vrácení neprošlo (admin, nebo <see cref="ExpiredReason"/>).</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Pojistka proti souběhu: admin převezme zboží ve chvíli, kdy vyprší termín – jen jeden zápis projde.</summary>
    public int Version { get; private set; }

    public static ReturnSaga Start(int orderId, DateTime startedAtUtc, TimeSpan sendWindow, bool requiresRefund)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Sága vratky potřebuje uloženou objednávku (s ID).");
        }

        return new ReturnSaga
        {
            OrderId = orderId,
            State = ReturnSagaState.AwaitingGoods,
            StartedAtUtc = startedAtUtc,
            UpdatedAtUtc = startedAtUtc,
            GoodsDeadlineUtc = startedAtUtc + sendWindow,
            RequiresRefund = requiresRefund
        };
    }

    /// <summary>Admin převzal zásilku v pořádku → sklad naskladní.</summary>
    public ReturnSagaAction GoodsReceived(DateTime nowUtc) => State == ReturnSagaState.AwaitingGoods
        ? MoveTo(ReturnSagaState.Restocking, nowUtc, ReturnSagaAction.RestockGoods)
        : ReturnSagaAction.None;

    /// <summary>Admin vrácení zamítl (zboží poškozené, nekompletní…).</summary>
    public ReturnSagaAction Reject(string reason, DateTime nowUtc)
    {
        if (State != ReturnSagaState.AwaitingGoods)
        {
            return ReturnSagaAction.None;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Zamítnutí vrácení potřebuje důvod (dostane ho zákazník).");
        }

        RejectionReason = reason.Trim();
        return MoveTo(ReturnSagaState.Rejected, nowUtc, ReturnSagaAction.RejectReturn);
    }

    /// <summary>Volá se pravidelně. Zboží nedorazilo do termínu → vrácení vyprší.</summary>
    public ReturnSagaAction TimedOut(DateTime nowUtc)
    {
        if (State != ReturnSagaState.AwaitingGoods || nowUtc < GoodsDeadlineUtc)
        {
            return ReturnSagaAction.None;
        }

        RejectionReason = ExpiredReason;
        return MoveTo(ReturnSagaState.Expired, nowUtc, ReturnSagaAction.RejectReturn);
    }

    /// <summary>Sklad naskladnil → kartou vrátit peníze, dobírka hotovo (peníze vrací admin ručně).</summary>
    public ReturnSagaAction GoodsRestocked(DateTime nowUtc)
    {
        if (State != ReturnSagaState.Restocking)
        {
            return ReturnSagaAction.None;
        }

        return RequiresRefund
            ? MoveTo(ReturnSagaState.Refunding, nowUtc, ReturnSagaAction.RefundPayment)
            : MoveTo(ReturnSagaState.Completed, nowUtc, ReturnSagaAction.CompleteReturn);
    }

    /// <summary>Payments vrátil peníze → vrácení je dokončené.</summary>
    public ReturnSagaAction PaymentRefunded(DateTime nowUtc) => State == ReturnSagaState.Refunding
        ? MoveTo(ReturnSagaState.Completed, nowUtc, ReturnSagaAction.CompleteReturn)
        : ReturnSagaAction.None;

    private ReturnSagaAction MoveTo(ReturnSagaState state, DateTime nowUtc, ReturnSagaAction action)
    {
        State = state;
        UpdatedAtUtc = nowUtc;
        Version++;
        return action;
    }
}

/// <remarks>Ukládá se jako text – nové hodnoty jen na konec.</remarks>
public enum ReturnSagaState
{
    /// <summary>Čeká se na vrácené zboží (s termínem).</summary>
    AwaitingGoods,

    /// <summary>Admin zboží převzal, příkaz RestockReturnedGoods odešel skladu.</summary>
    Restocking,

    /// <summary>Naskladněno, příkaz RefundPayment odešel modulu Payments.</summary>
    Refunding,

    /// <summary>Konec: zboží zpět na skladu, peníze vrácené (nebo je vrací admin ručně).</summary>
    Completed,

    /// <summary>Konec: admin vrácení zamítl.</summary>
    Rejected,

    /// <summary>Konec: zboží nedorazilo včas.</summary>
    Expired
}

/// <summary>Co má orchestrátor vratky udělat po přechodu ságy.</summary>
public enum ReturnSagaAction
{
    None,

    /// <summary>Poslat skladu RestockReturnedGoodsV1.</summary>
    RestockGoods,

    /// <summary>Objednávka zpět do Completed s důvodem (Order.RejectReturn).</summary>
    RejectReturn,

    /// <summary>Poslat RefundPaymentV1 (stejný příkaz jako u pozdní platby).</summary>
    RefundPayment,

    /// <summary>Objednávka je vrácená (Order.CompleteReturn).</summary>
    CompleteReturn
}
