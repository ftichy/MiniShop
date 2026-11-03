using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Saga;

/// <summary>
/// Stav ságy objednávky (krok 9, orchestrace) – "deník" orchestrátoru: v jakém kroku proces je a co má udělat dál.
/// Je to stavový automat: každá metoda = jedna zpráva, která může přijít; vrací akci, kterou má orchestrátor provést.
/// </summary>
/// <remarks>
/// <code>
/// AwaitingReservation ──StockReserved──────► Reserved ──OrderShipped──► Shipped ──OrderCompleted──► Completed
///        │                                      │
///        ├─StockReservationFailed─┐             └─OrderCancelled──► ReleasingStock ──StockReleased──► Released
///        └─timeout────────────────┴► Rejected ──StockReserved (pozdě!)──┘
/// </code>
/// E-shop krok 6: balení ságu nezajímá (zrušit jde dál, kompenzace stejná). Odeslání ano – po něm už zboží
/// nevracíme; sklad si rezervaci sám označí jako vydanou podle události OrderShippedV1 (sága nic neposílá).
/// E-shop krok 8: platba kartou je druhý účastník ságy:
/// <code>
/// AwaitingReservation ──StockReserved──► AwaitingPayment ──PaymentSucceeded──► Reserved ──► …
///                                            └─PaymentFailed / timeout platby──► ReleasingStock (+ zamítnout objednávku)
/// Rejected / ReleasingStock / Released ──PaymentSucceeded (pozdě!)──► vrátit peníze (RefundPayment)
/// </code>
/// E-shop krok 9: zrušit jde kdykoli před odesláním. Sága zrušení zaznamená a pozdní odpovědi dožene kompenzací:
/// <code>
/// AwaitingReservation ──OrderCancelled──► Cancelled ──StockReserved (pozdě!)──► ReleasingStock
/// AwaitingPayment ─────OrderCancelled──► ReleasingStock (pozdní PaymentSucceeded → RefundPayment)
/// Reserved (zaplaceno kartou) ──OrderCancelled──► ReleasingStock + vrátit peníze
/// </code>
/// Zpráva, která do aktuálního stavu nepatří (duplicita, zpoždění), vrátí <see cref="OrderSagaAction.None"/> – nic se nestane.
/// Sága nezná RabbitMQ ani kontrakty, jen rozhoduje. Provedení (poslat příkaz, potvrdit objednávku) je na orchestrátoru.
/// </remarks>
public sealed class OrderSaga
{
    public const string TimeoutReason = "Sklad neodpověděl včas.";

    /// <summary>E-shop krok 8: důvod zamítnutí, když zákazník nezaplatil včas.</summary>
    public const string PaymentTimeoutReason = "Platba nebyla dokončena včas – rezervované zboží jsme vrátili na sklad.";

    // Konstruktor pro EF Core.
    private OrderSaga()
    {
    }

    /// <summary>ID objednávky = ID ságy (jedna objednávka, jedna sága).</summary>
    public int OrderId { get; private set; }

    public OrderSagaState State { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Do kdy musí sklad odpovědět; pak sága objednávku zamítne (timeout).</summary>
    public DateTime ReservationDeadlineUtc { get; private set; }

    /// <summary>Důvod zamítnutí – od skladu, nebo <see cref="TimeoutReason"/>.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>Pojistka proti souběhu (concurrency token): timeout a odpověď skladu nesmí ságu změnit zároveň.</summary>
    public int Version { get; private set; }

    /// <summary>E-shop krok 8: platí se kartou – po rezervaci čekat na platbu (dobírka a API objednávky ne).</summary>
    public bool RequiresPayment { get; private set; }

    /// <summary>E-shop krok 8: do kdy musí zákazník zaplatit; pak sága objednávku zamítne a vrátí zboží.</summary>
    public DateTime? PaymentDeadlineUtc { get; private set; }

    /// <summary>E-shop krok 8: pozdní platbu už jsme poslali vrátit – podruhé ne.</summary>
    public bool RefundRequested { get; private set; }

    /// <summary>Založí ságu pro novou objednávku. Orchestrátor hned pošle příkaz ReserveStock.</summary>
    public static OrderSaga Start(int orderId, DateTime startedAtUtc, TimeSpan reservationTimeout, bool requiresPayment = false)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Sága potřebuje uloženou objednávku (s ID).");
        }

        return new OrderSaga
        {
            OrderId = orderId,
            State = OrderSagaState.AwaitingReservation,
            StartedAtUtc = startedAtUtc,
            UpdatedAtUtc = startedAtUtc,
            ReservationDeadlineUtc = startedAtUtc + reservationTimeout,
            RequiresPayment = requiresPayment
        };
    }

    /// <param name="paymentTimeout">E-shop krok 8: kolik času má zákazník na platbu kartou (od rezervace).</param>
    public OrderSagaAction StockReserved(DateTime nowUtc, TimeSpan paymentTimeout = default) => State switch
    {
        // E-shop krok 8: kartou → nejdřív platba (zboží zůstává rezervované), teprve pak potvrzení.
        OrderSagaState.AwaitingReservation when RequiresPayment => AwaitPayment(nowUtc, paymentTimeout),
        OrderSagaState.AwaitingReservation => MoveTo(OrderSagaState.Reserved, nowUtc, OrderSagaAction.ConfirmOrder),

        // Rezervace dorazila až po timeoutu (nebo po zrušení – e-shop krok 9) – objednávka už neplatí, zboží musí zpátky.
        OrderSagaState.Rejected or OrderSagaState.Cancelled
            => MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStock),

        _ => OrderSagaAction.None
    };

    public OrderSagaAction StockReservationFailed(string reason, DateTime nowUtc)
    {
        if (State != OrderSagaState.AwaitingReservation)
        {
            return OrderSagaAction.None;
        }

        RejectionReason = reason;
        return MoveTo(OrderSagaState.Rejected, nowUtc, OrderSagaAction.RejectOrder);
    }

    /// <summary>Volá se pravidelně. Když sklad do termínu neodpověděl, objednávka se zamítne.</summary>
    public OrderSagaAction ReservationTimedOut(DateTime nowUtc)
    {
        if (State != OrderSagaState.AwaitingReservation || nowUtc < ReservationDeadlineUtc)
        {
            return OrderSagaAction.None;
        }

        RejectionReason = TimeoutReason;
        return MoveTo(OrderSagaState.Rejected, nowUtc, OrderSagaAction.RejectOrder);
    }

    /// <summary>E-shop krok 6: zboží odešlo – rezervace se už neuvolní.</summary>
    public OrderSagaAction OrderShipped(DateTime nowUtc) => State == OrderSagaState.Reserved
        ? MoveTo(OrderSagaState.Shipped, nowUtc, OrderSagaAction.None)
        : OrderSagaAction.None;

    /// <summary>Doručeno (e-shop krok 6 – dřív „vyřízeno“ rovnou z Reserved).</summary>
    public OrderSagaAction OrderCompleted(DateTime nowUtc) => State == OrderSagaState.Shipped
        ? MoveTo(OrderSagaState.Completed, nowUtc, OrderSagaAction.None)
        : OrderSagaAction.None;

    /// <summary>
    /// Zákazník (nebo admin) objednávku zrušil – vrátit, co už je zarezervované nebo zaplacené (kompenzace).
    /// E-shop krok 9: i když sklad nebo brána ještě neodpověděli – pozdní odpověď dožene <see cref="StockReserved"/>
    /// a <see cref="PaymentSucceeded"/>.
    /// </summary>
    public OrderSagaAction OrderCancelled(DateTime nowUtc)
    {
        switch (State)
        {
            case OrderSagaState.AwaitingReservation:
                // Sklad ještě nic nepotvrdil – není co vracet. Zapsat si zrušení; pozdní rezervaci uvolníme.
                return MoveTo(OrderSagaState.Cancelled, nowUtc, OrderSagaAction.None);

            case OrderSagaState.AwaitingPayment:
                // Zboží zpět. Kdyby zákazník přesto zaplatil, PaymentSucceeded peníze vrátí (ReleasingStock/Released).
                return MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStock);

            case OrderSagaState.Reserved when RequiresPayment:
                // Kartou se do Reserved dostane jen zaplacená objednávka – vrátit zboží i peníze (jen jednou).
                RefundRequested = true;
                return MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStockAndRefundPayment);

            case OrderSagaState.Reserved:
                return MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStock);

            default:
                return OrderSagaAction.None;
        }
    }

    public OrderSagaAction StockReleased(DateTime nowUtc) => State == OrderSagaState.ReleasingStock
        ? MoveTo(OrderSagaState.Released, nowUtc, OrderSagaAction.None)
        : OrderSagaAction.None;

    /// <summary>E-shop krok 8: brána potvrdila platbu → potvrdit objednávku; pozdě (už zamítnutá) → vrátit peníze.</summary>
    public OrderSagaAction PaymentSucceeded(DateTime nowUtc)
    {
        if (State == OrderSagaState.AwaitingPayment)
        {
            return MoveTo(OrderSagaState.Reserved, nowUtc, OrderSagaAction.ConfirmOrder);
        }

        // Zaplatil po termínu: objednávka je zamítnutá a zboží (skoro) zpátky na skladě – peníze musí zpátky taky.
        if (State is OrderSagaState.Rejected or OrderSagaState.ReleasingStock or OrderSagaState.Released
            && RequiresPayment && !RefundRequested)
        {
            RefundRequested = true;
            UpdatedAtUtc = nowUtc;
            Version++;
            return OrderSagaAction.RefundPayment;
        }

        return OrderSagaAction.None;
    }

    /// <summary>E-shop krok 8: brána platbu zamítla → zamítnout objednávku a vrátit zboží (kompenzace).</summary>
    public OrderSagaAction PaymentFailed(string reason, DateTime nowUtc)
    {
        if (State != OrderSagaState.AwaitingPayment)
        {
            return OrderSagaAction.None;
        }

        RejectionReason = reason;
        return MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.RejectOrderAndReleaseStock);
    }

    /// <summary>E-shop krok 8: volá se pravidelně. Kdo do termínu nezaplatil, tomu se objednávka zamítne a zboží vrátí.</summary>
    public OrderSagaAction PaymentTimedOut(DateTime nowUtc)
    {
        if (State != OrderSagaState.AwaitingPayment || nowUtc < PaymentDeadlineUtc)
        {
            return OrderSagaAction.None;
        }

        RejectionReason = PaymentTimeoutReason;
        return MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.RejectOrderAndReleaseStock);
    }

    /// <summary>Termín, který teď běží (rezervace nebo platba) – pro timeout worker.</summary>
    public OrderSagaAction TimedOut(DateTime nowUtc) => State == OrderSagaState.AwaitingPayment
        ? PaymentTimedOut(nowUtc)
        : ReservationTimedOut(nowUtc);

    private OrderSagaAction AwaitPayment(DateTime nowUtc, TimeSpan paymentTimeout)
    {
        if (paymentTimeout <= TimeSpan.Zero)
        {
            throw new DomainException("Platba kartou potřebuje termín.");
        }

        PaymentDeadlineUtc = nowUtc + paymentTimeout;
        return MoveTo(OrderSagaState.AwaitingPayment, nowUtc, OrderSagaAction.RequestPayment);
    }

    private OrderSagaAction MoveTo(OrderSagaState state, DateTime nowUtc, OrderSagaAction action)
    {
        State = state;
        UpdatedAtUtc = nowUtc;
        Version++;
        return action;
    }
}

public enum OrderSagaState
{
    /// <summary>Příkaz ReserveStock odešel, čeká se na odpověď skladu.</summary>
    AwaitingReservation,

    /// <summary>Zboží zarezervováno, objednávka potvrzená.</summary>
    Reserved,

    /// <summary>Objednávka zamítnutá (sklad odmítl nebo neodpověděl včas).</summary>
    Rejected,

    /// <summary>Příkaz ReleaseStock odešel, čeká se na potvrzení uvolnění.</summary>
    ReleasingStock,

    /// <summary>Konec: objednávka vyřízena, zboží zůstává prodané.</summary>
    Completed,

    /// <summary>Konec: rezervace uvolněna, zboží je zpět na skladu.</summary>
    Released,

    /// <summary>E-shop krok 6: zboží odešlo (rezervace je na skladě vydaná), čeká se na doručení.</summary>
    Shipped,

    /// <summary>E-shop krok 8: zboží zarezervované, příkaz RequestPayment odešel, čeká se na platbu (s termínem).</summary>
    AwaitingPayment,

    /// <summary>
    /// E-shop krok 9: zrušeno dřív, než sklad odpověděl – nic se nevrací. Pozdní rezervace se ještě uvolní (ReleasingStock).
    /// </summary>
    Cancelled
}

/// <summary>Co má orchestrátor udělat po přechodu ságy.</summary>
public enum OrderSagaAction
{
    None,
    ConfirmOrder,
    RejectOrder,
    ReleaseStock,

    /// <summary>E-shop krok 8: objednávka čeká na platbu – poslat RequestPaymentV1.</summary>
    RequestPayment,

    /// <summary>E-shop krok 8: platba neproběhla – zamítnout objednávku a zároveň vrátit zboží (ReleaseStockV1).</summary>
    RejectOrderAndReleaseStock,

    /// <summary>E-shop krok 8: platba přišla pozdě – vrátit peníze (RefundPaymentV1).</summary>
    RefundPayment,

    /// <summary>E-shop krok 9: zrušená zaplacená objednávka – vrátit zboží (ReleaseStockV1) i peníze (RefundPaymentV1).</summary>
    ReleaseStockAndRefundPayment
}
