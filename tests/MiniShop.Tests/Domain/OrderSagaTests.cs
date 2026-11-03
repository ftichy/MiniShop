using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Tests.Domain;

/// <summary>
/// Krok 9: stavový automat ságy objednávky – bez databáze, bez zpráv. Každý test = jedna cesta diagramem
/// v komentáři u <see cref="OrderSaga"/>.
/// </summary>
public class OrderSagaTests
{
    private static readonly DateTime Start = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static OrderSaga NewSaga() => OrderSaga.Start(orderId: 42, Start, Timeout);

    [Fact]
    public void Start_CekaNaSkladSTerminem()
    {
        var saga = NewSaga();

        Assert.Equal(OrderSagaState.AwaitingReservation, saga.State);
        Assert.Equal(Start + Timeout, saga.ReservationDeadlineUtc);
    }

    [Fact]
    public void Start_BezIdObjednavky_VyhodiVyjimku()
    {
        Assert.Throws<DomainException>(() => OrderSaga.Start(0, Start, Timeout));
    }

    [Fact]
    public void Rezervovano_PotvrditObjednavku()
    {
        var saga = NewSaga();

        Assert.Equal(OrderSagaAction.ConfirmOrder, saga.StockReserved(Start));
        Assert.Equal(OrderSagaState.Reserved, saga.State);
    }

    [Fact]
    public void RezervaceSelhala_ZamitnoutSDuvodemSkladu()
    {
        var saga = NewSaga();

        Assert.Equal(OrderSagaAction.RejectOrder, saga.StockReservationFailed("Nedostatek zboží", Start));
        Assert.Equal(OrderSagaState.Rejected, saga.State);
        Assert.Equal("Nedostatek zboží", saga.RejectionReason);
    }

    [Fact]
    public void Zruseni_UvolnitRezervaci_PakKonec()
    {
        var saga = NewSaga();
        saga.StockReserved(Start);

        Assert.Equal(OrderSagaAction.ReleaseStock, saga.OrderCancelled(Start));
        Assert.Equal(OrderSagaState.ReleasingStock, saga.State);

        Assert.Equal(OrderSagaAction.None, saga.StockReleased(Start));
        Assert.Equal(OrderSagaState.Released, saga.State);
    }

    [Fact]
    public void Odeslani_PakDoruceni_Konec()
    {
        var saga = NewSaga();
        saga.StockReserved(Start);

        Assert.Equal(OrderSagaAction.None, saga.OrderCompleted(Start));   // e-shop krok 6: doručit jde až po odeslání
        Assert.Equal(OrderSagaState.Reserved, saga.State);

        Assert.Equal(OrderSagaAction.None, saga.OrderShipped(Start));     // sklad vydá rezervaci sám (OrderShippedV1)
        Assert.Equal(OrderSagaState.Shipped, saga.State);
        Assert.Equal(OrderSagaAction.None, saga.OrderCancelled(Start));   // po odeslání se zboží nevrací
        Assert.Equal(OrderSagaState.Shipped, saga.State);

        Assert.Equal(OrderSagaAction.None, saga.OrderCompleted(Start));
        Assert.Equal(OrderSagaState.Completed, saga.State);
    }

    [Fact]
    public void Timeout_PredTerminem_NicNedela()
    {
        var saga = NewSaga();

        Assert.Equal(OrderSagaAction.None, saga.ReservationTimedOut(Start + Timeout - TimeSpan.FromSeconds(1)));
        Assert.Equal(OrderSagaState.AwaitingReservation, saga.State);
    }

    [Fact]
    public void Timeout_PoTerminu_Zamitnout()
    {
        var saga = NewSaga();

        Assert.Equal(OrderSagaAction.RejectOrder, saga.ReservationTimedOut(Start + Timeout));
        Assert.Equal(OrderSagaState.Rejected, saga.State);
        Assert.Equal(OrderSaga.TimeoutReason, saga.RejectionReason);
    }

    [Fact]
    public void PozdniRezervacePoTimeoutu_UvolnitZbozi()
    {
        var saga = NewSaga();
        saga.ReservationTimedOut(Start + Timeout);

        Assert.Equal(OrderSagaAction.ReleaseStock, saga.StockReserved(Start + Timeout));
        Assert.Equal(OrderSagaState.ReleasingStock, saga.State);
    }

    [Fact]
    public void PozdniOdmitnutiPoTimeoutu_NicNedela()
    {
        var saga = NewSaga();
        saga.ReservationTimedOut(Start + Timeout);

        Assert.Equal(OrderSagaAction.None, saga.StockReservationFailed("Nedostatek zboží", Start + Timeout));
        Assert.Equal(OrderSaga.TimeoutReason, saga.RejectionReason);
    }

    [Fact]
    public void DuplicitniOdpoved_NicNedela()
    {
        var saga = NewSaga();
        saga.StockReserved(Start);

        Assert.Equal(OrderSagaAction.None, saga.StockReserved(Start));
        Assert.Equal(OrderSagaState.Reserved, saga.State);
    }

    [Fact]
    public void KazdyPrechod_ZvysiVerzi()
    {
        var saga = NewSaga();

        saga.StockReserved(Start);
        saga.StockReserved(Start);   // nic se nestalo → verze stejná

        Assert.Equal(1, saga.Version);
    }

    // --- E-shop krok 8: platba kartou ---

    private static readonly TimeSpan PaymentTimeout = TimeSpan.FromMinutes(30);

    private static OrderSaga CardSaga()
    {
        var saga = OrderSaga.Start(orderId: 42, Start, Timeout, requiresPayment: true);
        Assert.Equal(OrderSagaAction.RequestPayment, saga.StockReserved(Start, PaymentTimeout));
        return saga;
    }

    [Fact]
    public void Platba_PoRezervaciCekaNaPlatbuSTerminem()
    {
        var saga = CardSaga();

        Assert.Equal(OrderSagaState.AwaitingPayment, saga.State);
        Assert.Equal(Start + PaymentTimeout, saga.PaymentDeadlineUtc);
    }

    [Fact]
    public void Platba_Zaplaceno_Potvrdit_DruhaZpravaNicNeudela()
    {
        var saga = CardSaga();

        Assert.Equal(OrderSagaAction.ConfirmOrder, saga.PaymentSucceeded(Start));
        Assert.Equal(OrderSagaState.Reserved, saga.State);
        Assert.Equal(OrderSagaAction.None, saga.PaymentSucceeded(Start));   // duplicita – nic (a žádné vrácení peněz)
    }

    [Fact]
    public void Platba_Zamitnuta_ZamitnoutAVratitZbozi()
    {
        var saga = CardSaga();

        Assert.Equal(OrderSagaAction.RejectOrderAndReleaseStock, saga.PaymentFailed("Karta zamítnuta", Start));
        Assert.Equal((OrderSagaState.ReleasingStock, "Karta zamítnuta"), (saga.State, saga.RejectionReason));
    }

    [Fact]
    public void Platba_Timeout_AzPoTerminu()
    {
        var saga = CardSaga();

        Assert.Equal(OrderSagaAction.None, saga.TimedOut(Start + PaymentTimeout - TimeSpan.FromSeconds(1)));
        Assert.Equal(OrderSagaAction.RejectOrderAndReleaseStock, saga.TimedOut(Start + PaymentTimeout));
        Assert.Equal(OrderSaga.PaymentTimeoutReason, saga.RejectionReason);
    }

    [Fact]
    public void Platba_PozdniPlatba_VratitPenizeJednou()
    {
        var saga = CardSaga();
        saga.TimedOut(Start + PaymentTimeout);
        saga.StockReleased(Start + PaymentTimeout);

        Assert.Equal(OrderSagaAction.RefundPayment, saga.PaymentSucceeded(Start + PaymentTimeout));
        Assert.Equal(OrderSagaAction.None, saga.PaymentSucceeded(Start + PaymentTimeout));   // vrací se jen jednou
        Assert.Equal(OrderSagaState.Released, saga.State);
    }

    [Fact]
    public void Platba_DobirkaPotvrdiHnedPoRezervaci()
    {
        var saga = OrderSaga.Start(orderId: 42, Start, Timeout, requiresPayment: false);

        Assert.Equal(OrderSagaAction.ConfirmOrder, saga.StockReserved(Start, PaymentTimeout));
        Assert.Equal(OrderSagaAction.None, saga.PaymentSucceeded(Start));   // platba sem nepatří
    }
}
