using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Saga;

/// <summary>
/// Stav ságy objednávky (krok 9, orchestrace) – "deník" orchestrátoru: v jakém kroku proces je a co má udělat dál.
/// Je to stavový automat: každá metoda = jedna zpráva, která může přijít; vrací akci, kterou má orchestrátor provést.
/// </summary>
/// <remarks>
/// <code>
/// AwaitingReservation ──StockReserved──────► Reserved ──OrderCompleted──► Completed
///        │                                      │
///        ├─StockReservationFailed─┐             └─OrderCancelled──► ReleasingStock ──StockReleased──► Released
///        └─timeout────────────────┴► Rejected ──StockReserved (pozdě!)──┘
/// </code>
/// Zpráva, která do aktuálního stavu nepatří (duplicita, zpoždění), vrátí <see cref="OrderSagaAction.None"/> – nic se nestane.
/// Sága nezná RabbitMQ ani kontrakty, jen rozhoduje. Provedení (poslat příkaz, potvrdit objednávku) je na orchestrátoru.
/// </remarks>
public sealed class OrderSaga
{
    public const string TimeoutReason = "Sklad neodpověděl včas.";

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

    /// <summary>Založí ságu pro novou objednávku. Orchestrátor hned pošle příkaz ReserveStock.</summary>
    public static OrderSaga Start(int orderId, DateTime startedAtUtc, TimeSpan reservationTimeout)
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
            ReservationDeadlineUtc = startedAtUtc + reservationTimeout
        };
    }

    public OrderSagaAction StockReserved(DateTime nowUtc) => State switch
    {
        OrderSagaState.AwaitingReservation => MoveTo(OrderSagaState.Reserved, nowUtc, OrderSagaAction.ConfirmOrder),

        // Rezervace dorazila až po timeoutu – objednávka je už zamítnutá, zboží musí zpátky (kompenzace).
        OrderSagaState.Rejected => MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStock),

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

    public OrderSagaAction OrderCompleted(DateTime nowUtc) => State == OrderSagaState.Reserved
        ? MoveTo(OrderSagaState.Completed, nowUtc, OrderSagaAction.None)
        : OrderSagaAction.None;

    /// <summary>Zákazník zrušil potvrzenou objednávku – rezervaci je třeba uvolnit (kompenzace).</summary>
    public OrderSagaAction OrderCancelled(DateTime nowUtc) => State == OrderSagaState.Reserved
        ? MoveTo(OrderSagaState.ReleasingStock, nowUtc, OrderSagaAction.ReleaseStock)
        : OrderSagaAction.None;

    public OrderSagaAction StockReleased(DateTime nowUtc) => State == OrderSagaState.ReleasingStock
        ? MoveTo(OrderSagaState.Released, nowUtc, OrderSagaAction.None)
        : OrderSagaAction.None;

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
    Released
}

/// <summary>Co má orchestrátor udělat po přechodu ságy.</summary>
public enum OrderSagaAction
{
    None,
    ConfirmOrder,
    RejectOrder,
    ReleaseStock
}
