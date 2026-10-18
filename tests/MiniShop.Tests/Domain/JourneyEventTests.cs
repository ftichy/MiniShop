using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Journeys;

namespace MiniShop.Tests.Domain;

/// <summary>Krok 12A: pravidla jedné události v proudu uživatele.</summary>
public sealed class JourneyEventTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DlouhyText_SeZkrati()
    {
        var e = new JourneyEvent(Guid.NewGuid(), Guid.NewGuid(), JourneyEventType.FieldChanged, 10,
            "Input.CustomerName", "Jméno", new string('x', 2_000), null, Now);

        Assert.Equal(JourneyEvent.MaxTextLength, e.Value!.Length);
    }

    [Fact]
    public void ZapornyCas_JeChyba() =>
        Assert.Throws<DomainException>(() =>
            new JourneyEvent(Guid.NewGuid(), Guid.NewGuid(), JourneyEventType.Clicked, -1, null, null, null, null, Now));

    [Fact]
    public void ChybejiciId_JeChyba() =>
        Assert.Throws<DomainException>(() =>
            new JourneyEvent(Guid.Empty, Guid.NewGuid(), JourneyEventType.Clicked, 0, null, null, null, null, Now));

    [Fact]
    public void OrderSubmitted_NeseCisloObjednavky()
    {
        var e = JourneyEvent.OrderSubmitted(Guid.NewGuid(), 5_000, orderId: 3, Now);

        Assert.Equal(JourneyEventType.OrderSubmitted, e.Type);
        Assert.Equal(3, e.OrderId);
        Assert.True(JourneyEvent.IsServerOnly(e.Type));
        Assert.False(JourneyEvent.IsServerOnly(JourneyEventType.FieldChanged));
    }
}
