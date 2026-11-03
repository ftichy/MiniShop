using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Tests.Domain;

/// <summary>
/// E-shop krok 12: stavový automat ságy vratky (stav × zpráva) a pravidla vrácení na objednávce – bez databáze a zpráv.
/// </summary>
public class ReturnSagaTests
{
    private static readonly DateTime Start = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan SendWindow = TimeSpan.FromDays(14);

    private static ReturnSaga Card() => ReturnSaga.Start(42, Start, SendWindow, requiresRefund: true);
    private static ReturnSaga CashOnDelivery() => ReturnSaga.Start(42, Start, SendWindow, requiresRefund: false);

    [Fact]
    public void Kartou_Prevzeti_Naskladneni_Penize_Konec()
    {
        var saga = Card();

        Assert.Equal(ReturnSagaAction.RestockGoods, saga.GoodsReceived(Start));
        Assert.Equal(ReturnSagaAction.RefundPayment, saga.GoodsRestocked(Start));
        Assert.Equal(ReturnSagaAction.CompleteReturn, saga.PaymentRefunded(Start));
        Assert.Equal(ReturnSagaState.Completed, saga.State);
    }

    [Fact]
    public void Dobirka_PoNaskladneniRovnouKonec()
    {
        var saga = CashOnDelivery();
        saga.GoodsReceived(Start);

        Assert.Equal(ReturnSagaAction.CompleteReturn, saga.GoodsRestocked(Start));
        Assert.Equal(ReturnSagaAction.None, saga.PaymentRefunded(Start));   // žádná platba se nevracela
    }

    [Fact]
    public void Duplicity_NicNedelaji()
    {
        var saga = Card();
        saga.GoodsReceived(Start);

        Assert.Equal(ReturnSagaAction.None, saga.GoodsReceived(Start));        // admin klikl dvakrát
        Assert.Equal(ReturnSagaAction.None, saga.PaymentRefunded(Start));      // peníze ještě neodešly
        saga.GoodsRestocked(Start);
        Assert.Equal(ReturnSagaAction.None, saga.GoodsRestocked(Start));       // druhá odpověď skladu
        Assert.Equal(2, saga.Version);
    }

    [Fact]
    public void Zamitnuti_JenPredPrevzetimASDuvodem()
    {
        var saga = Card();
        Assert.Throws<DomainException>(() => saga.Reject(" ", Start));

        Assert.Equal(ReturnSagaAction.RejectReturn, saga.Reject("Zboží je poškozené", Start));
        Assert.Equal((ReturnSagaState.Rejected, "Zboží je poškozené"), (saga.State, saga.RejectionReason));
        Assert.Equal(ReturnSagaAction.None, saga.GoodsReceived(Start));
    }

    [Fact]
    public void Timeout_AzPoTerminu_PrevzetiPakNejde()
    {
        var saga = Card();

        Assert.Equal(ReturnSagaAction.None, saga.TimedOut(Start + SendWindow - TimeSpan.FromMinutes(1)));
        Assert.Equal(ReturnSagaAction.RejectReturn, saga.TimedOut(Start + SendWindow));
        Assert.Equal((ReturnSagaState.Expired, ReturnSaga.ExpiredReason), (saga.State, saga.RejectionReason));
        Assert.Equal(ReturnSagaAction.None, saga.GoodsReceived(Start + SendWindow));
    }

    [Fact]
    public void Timeout_PoPrevzetiNic()
    {
        var saga = Card();
        saga.GoodsReceived(Start);

        Assert.Equal(ReturnSagaAction.None, saga.TimedOut(Start + SendWindow));   // zboží dorazilo, termín už nehraje roli
    }

    // --- pravidla na objednávce ---

    private static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private static Order Delivered()
    {
        var order = new Order("zakaznik-1", "Jan", "jan@example.com", Start);
        order.Confirm(Start);
        order.StartPacking(Start);
        order.Ship("PPL", "123", Start);
        order.MarkDelivered(Start);
        return order;
    }

    [Fact]
    public void Objednavka_VratitJenDoTerminuAJenJednou()
    {
        var order = Delivered();
        Assert.Throws<DomainException>(() => order.RequestReturn("", Start, Window));          // bez důvodu ne
        Assert.False(order.CanRequestReturn(Start + Window + TimeSpan.FromSeconds(1), Window));

        order.RequestReturn("Nesedí velikost", Start + Window, Window);
        Assert.Equal(OrderStatus.ReturnRequested, order.Status);
        Assert.True(order.IsActive);   // rozpracovaná – účet zatím nejde smazat

        order.RejectReturn("Zboží je poškozené", Start + Window);
        Assert.Equal((OrderStatus.Completed, "Zboží je poškozené"), (order.Status, order.ReturnRejectionReason));
        Assert.Throws<DomainException>(() => order.RequestReturn("Znovu", Start + Window, Window));   // jen jednou
    }

    [Fact]
    public void Objednavka_NedorucenaNejdeVratit_VracenaJeKonecna()
    {
        var pending = new Order("zakaznik-1", "Jan", "jan@example.com", Start);
        Assert.Throws<DomainException>(() => pending.RequestReturn("Nechci", Start, Window));

        var order = Delivered();
        order.RequestReturn("Nesedí velikost", Start, Window);
        order.CompleteReturn(Start);

        Assert.Equal(OrderStatus.Returned, order.Status);
        Assert.False(order.IsActive);
        Assert.Throws<DomainException>(() => order.Cancel(Start));
    }
}
