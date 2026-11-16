using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Inventory;

namespace MiniShop.Tests.Domain;

/// <summary>E-shop krok 22: rezervace skladu a vratky po částech.</summary>
public sealed class StockReservationTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static StockReservation Fulfilled(int quantity = 3)
    {
        var reservation = new StockReservation(42, 7, quantity, Now);
        reservation.Fulfil(Now);
        return reservation;
    }

    [Fact]
    public void VratkaPoCastech_PosledniKusUzavreRezervaci()
    {
        var reservation = Fulfilled(3);

        reservation.Return(1, Now);
        Assert.Equal((1, 2, true), (reservation.ReturnedQuantity, reservation.ReturnableQuantity, reservation.CanBeReturned));
        Assert.Null(reservation.ReturnedAtUtc);

        reservation.Return(2, Now);
        Assert.Equal((3, 0, false), (reservation.ReturnedQuantity, reservation.ReturnableQuantity, reservation.CanBeReturned));
        Assert.Equal(Now, reservation.ReturnedAtUtc);
        Assert.Throws<DomainException>(() => reservation.Return(1, Now));
    }

    [Fact]
    public void VratkaPoCastech_NejdeViceNezZbyva_NiNevydane()
    {
        var reservation = Fulfilled(2);
        Assert.Throws<DomainException>(() => reservation.Return(3, Now));
        Assert.Throws<DomainException>(() => reservation.Return(0, Now));
        Assert.Throws<DomainException>(() => new StockReservation(42, 7, 2, Now).Return(1, Now));   // ještě neodešlo
    }

    [Fact]
    public void CelaVratkaPoCastecne_VratiJenZbytek()
    {
        var reservation = Fulfilled(3);
        reservation.Return(1, Now);

        Assert.Equal(2, reservation.MarkReturned(Now));
        Assert.Equal(3, reservation.ReturnedQuantity);
        Assert.Throws<DomainException>(() => reservation.MarkReturned(Now));
    }
}
