using MiniShop.Contracts;
using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Orders;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Tests.Contracts;

/// <summary>
/// Krok 7: integrační události jsou veřejné API. Testy hlídají, aby se jejich tvar omylem nezměnil
/// a aby příjemce zvládl zprávu z novější verze (tolerant reader).
/// </summary>
public sealed class IntegrationContractTests
{
    private static readonly Guid EventId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime OccurredAtUtc = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Kontraktový test: když někdo přejmenuje nebo odebere pole, JSON se změní a test spadne.
    /// Správná reakce není upravit očekávaný JSON, ale vytvořit OrderPlacedV2.
    /// </summary>
    [Fact]
    public void OrderPlacedV1_TvarJsonuSeNezmenil()
    {
        var placed = new OrderPlacedV1(EventId, OccurredAtUtc, 42, "jan@example.com", 300m, [new OrderLineV1(7, 3)]);

        var json = OutboxMessageFactory.Create(placed).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"CustomerEmail":"jan@example.com","TotalPrice":300,"Lines":[{"ProductId":7,"Quantity":3}]}""",
            json);
    }

    [Fact]
    public void OrderCancelledV1_TvarJsonuSeNezmenil()
    {
        var cancelled = new OrderCancelledV1(EventId, OccurredAtUtc, 42, "jan@example.com", [new OrderLineV1(7, 3)]);

        var json = OutboxMessageFactory.Create(cancelled).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"CustomerEmail":"jan@example.com","Lines":[{"ProductId":7,"Quantity":3}]}""",
            json);
    }

    [Fact]
    public void Outbox_IntegracniUdalostUloziSNazvemKontraktuAVerzi()
    {
        var completed = new OrderCompletedV1(EventId, OccurredAtUtc, 42, "jan@example.com");

        var message = OutboxMessageFactory.Create(completed);

        Assert.Equal(OutboxMessageKind.Integration, message.Kind);
        Assert.Equal("OrderCompletedV1", message.Type);
        Assert.Equal(EventId, message.Id);
        Assert.Equal(completed, IntegrationMessageReader.Read(message));   // tam a zpět beze ztráty
    }

    /// <summary>
    /// Tolerant reader: odesílatel už posílá pole navíc (třeba po přidání volitelného pole do kontraktu),
    /// příjemce se starší verzí ho ignoruje a zprávu zpracuje.
    /// </summary>
    [Fact]
    public void Reader_NeznamaPoleIgnoruje()
    {
        var message = new OutboxMessage
        {
            Id = EventId,
            Kind = OutboxMessageKind.Integration,
            Type = "OrderCompletedV1",
            Payload = """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"CustomerEmail":"jan@example.com","CompletedBy":"sklad-praha"}""",
            OccurredAtUtc = OccurredAtUtc
        };

        var completed = Assert.IsType<OrderCompletedV1>(IntegrationMessageReader.Read(message));

        Assert.Equal(42, completed.OrderId);
        Assert.Equal("jan@example.com", completed.CustomerEmail);
    }

    [Fact]
    public void Reader_NeznamyKontrakt_VyhodiVyjimku()
    {
        var message = new OutboxMessage { Id = EventId, Kind = OutboxMessageKind.Integration, Type = "OrderShippedV1", Payload = "{}" };

        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationMessageReader.Read(message));
        Assert.Contains("OrderShippedV1", ex.Message);
    }

    /// <summary>Konvence: každý kontrakt nese verzi v názvu (…V1, …V2), aby šly verze publikovat vedle sebe.</summary>
    [Fact]
    public void VsechnyKontrakty_MajiVerziVNazvu()
    {
        var contracts = typeof(IIntegrationMessage).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(IIntegrationMessage).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(contracts);
        Assert.All(contracts, t => Assert.Matches(@"V\d+$", t.Name));
    }

    /// <summary>Krok 9: příkaz pro sklad je také kontrakt – jeho tvar hlídá stejný test.</summary>
    [Fact]
    public void ReserveStockV1_TvarJsonuSeNezmenil()
    {
        var reserve = new ReserveStockV1(EventId, OccurredAtUtc, 42, [new StockLineV1(7, 3)]);

        var json = OutboxMessageFactory.Create(reserve).Payload;

        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Lines":[{"ProductId":7,"Quantity":3}]}""",
            json);
    }

    /// <summary>Žádost Orderingu o celý katalog (obnova kopie katalogu).</summary>
    [Fact]
    public void SyncCatalogV1_TvarJsonuSeNezmenil()
    {
        var sync = new SyncCatalogV1(EventId, OccurredAtUtc);

        var json = OutboxMessageFactory.Create(sync).Payload;

        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z"}""",
            json);
    }

    [Fact]
    public void StockReservationFailedV1_TvarJsonuSeNezmenil()
    {
        var failed = new StockReservationFailedV1(EventId, OccurredAtUtc, 42, "Nedostatek");

        var json = OutboxMessageFactory.Create(failed).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Reason":"Nedostatek"}""",
            json);
    }

    /// <summary>
    /// MessageId je společné jméno pro EventId / CommandId (krok 9). Do JSONu se nedostane –
    /// kontrakty z kroku 7 se přidáním IIntegrationMessage nezměnily.
    /// </summary>
    [Fact]
    public void MessageId_JeEventIdNeboCommandIdANeniVJsonu()
    {
        IIntegrationMessage @event = new StockReservedV1(EventId, OccurredAtUtc, 42);
        IIntegrationMessage command = new ReleaseStockV1(EventId, OccurredAtUtc, 42);

        Assert.Equal(EventId, @event.MessageId);
        Assert.Equal(EventId, command.MessageId);
        Assert.DoesNotContain("MessageId", OutboxMessageFactory.Create(@event).Payload);
        Assert.DoesNotContain("MessageId", OutboxMessageFactory.Create(command).Payload);
    }

    [Fact]
    public void Outbox_PrikazJeIntegracniZpravaSIdPrikazu()
    {
        var release = new ReleaseStockV1(EventId, OccurredAtUtc, 42);

        var message = OutboxMessageFactory.Create(release);

        Assert.Equal(OutboxMessageKind.Integration, message.Kind);
        Assert.Equal("ReleaseStockV1", message.Type);
        Assert.Equal(EventId, message.Id);
        Assert.Equal(release, IntegrationMessageReader.Read(message));
    }
}
