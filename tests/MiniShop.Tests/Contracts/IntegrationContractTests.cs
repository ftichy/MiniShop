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

    /// <summary>E-shop krok 6: odeslání objednávky – čte ho sklad (výdej rezervace) a v kroku 7 notifikace.</summary>
    [Fact]
    public void OrderShippedV1_TvarJsonuSeNezmenil()
    {
        var shipped = new OrderShippedV1(EventId, OccurredAtUtc, 42, "jan@example.com", "PPL", "ZAS-42");

        var json = OutboxMessageFactory.Create(shipped).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"CustomerEmail":"jan@example.com","Carrier":"PPL","TrackingNumber":"ZAS-42"}""",
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
        var message = new OutboxMessage { Id = EventId, Kind = OutboxMessageKind.Integration, Type = "OrderTeleportedV1", Payload = "{}" };

        var ex = Assert.Throws<InvalidOperationException>(() => IntegrationMessageReader.Read(message));
        Assert.Contains("OrderTeleportedV1", ex.Message);
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

    /// <summary>E-shop krok 22: vratka po částech – příkazy a odpovědi skladu a Payments s číslem vratky.</summary>
    [Fact]
    public void VratkaPoCastechV2_TvarJsonuSeNezmenil()
    {
        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"Lines":[{"ProductId":7,"Quantity":1}]}""",
            OutboxMessageFactory.Create(new RestockReturnedGoodsV2(EventId, OccurredAtUtc, 42, 2, [new StockLineV1(7, 1)])).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"Quantity":1}""",
            OutboxMessageFactory.Create(new ReturnedGoodsRestockedV2(EventId, OccurredAtUtc, 42, 2, 1)).Payload);
        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"Amount":99.5}""",
            OutboxMessageFactory.Create(new MiniShop.Contracts.Payments.RefundPaymentV2(EventId, OccurredAtUtc, 42, 2, 99.5m)).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"Amount":99.5,"TotalRefunded":150}""",
            OutboxMessageFactory.Create(new MiniShop.Contracts.Payments.PaymentRefundedV2(EventId, OccurredAtUtc, 42, 2, 99.5m, 150m)).Payload);
    }

    /// <summary>E-shop krok 22B: vratka po částech – zprávy Orderingu (posílat je začne krok 22C).</summary>
    [Fact]
    public void VratkaPoCastechOrderingV2_TvarJsonuSeNezmenil()
    {
        ReturnItemV1[] items = [new ReturnItemV1(7, "Mouse", 1, 100m, 10m, 90m)];
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"CustomerEmail":"jan@example.com","Reason":"Size","SendByUtc":"2026-01-29T10:00:00Z","Items":[{"ProductId":7,"ProductName":"Mouse","Quantity":1,"UnitPrice":100,"Discount":10,"LineTotal":90}]}""",
            OutboxMessageFactory.Create(new ReturnRequestedV2(EventId, OccurredAtUtc, 42, 2, "jan@example.com", "Size", OccurredAtUtc.AddDays(14), items)).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"CustomerEmail":"jan@example.com","Reason":"Broken"}""",
            OutboxMessageFactory.Create(new ReturnRejectedV2(EventId, OccurredAtUtc, 42, 2, "jan@example.com", "Broken")).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ReturnNumber":2,"CustomerEmail":"jan@example.com","Items":[{"ProductId":7,"ProductName":"Mouse","Quantity":1,"UnitPrice":100,"Discount":10,"LineTotal":90}],"ShippingRefund":0,"Amount":90,"RefundedByCard":true,"FullyReturnedProductIds":[7]}""",
            OutboxMessageFactory.Create(new ReturnCompletedV2(EventId, OccurredAtUtc, 42, 2, "jan@example.com", items, 0m, 90m, true, [7])).Payload);
    }

    /// <summary>E-shop krok 22C: vyřízené vratky po částech při přestavění přehledu tržeb.</summary>
    [Fact]
    public void OrderPartialReturnsSnapshotV1_TvarJsonuSeNezmenil()
    {
        var snapshot = new OrderPartialReturnsSnapshotV1(EventId, OccurredAtUtc, 42,
            [new PartialReturnSnapshotV1(1, 90m, [new ReturnItemV1(7, "Mouse", 1, 100m, 10m, 90m)])]);

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Returns":[{"ReturnNumber":1,"Amount":90,"Items":[{"ProductId":7,"ProductName":"Mouse","Quantity":1,"UnitPrice":100,"Discount":10,"LineTotal":90}]}]}""",
            OutboxMessageFactory.Create(snapshot).Payload);
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

    /// <summary>E-shop krok 26B: předání zásilky dopravci – příkaz a obě odpovědi.</summary>
    [Fact]
    public void ZasilkaUDopravce_TvarJsonuSeNezmenil()
    {
        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"RecipientName":"Jan Novak","Street":"Dlouha 5","City":"Praha","PostalCode":"11000","CashOnDelivery":399}""",
            OutboxMessageFactory.Create(new MiniShop.Contracts.Shipping.CreateShipmentV1(EventId, OccurredAtUtc, 42, "Jan Novak", "Dlouha 5", "Praha", "11000", 399m)).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Carrier":"Simulator","TrackingNumber":"CS0123456789"}""",
            OutboxMessageFactory.Create(new MiniShop.Contracts.Shipping.ShipmentCreatedV1(EventId, OccurredAtUtc, 42, "Simulator", "CS0123456789")).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Reason":"Mimo oblast"}""",
            OutboxMessageFactory.Create(new MiniShop.Contracts.Shipping.ShipmentFailedV1(EventId, OccurredAtUtc, 42, "Mimo oblast")).Payload);
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

    /// <summary>E-shop krok 2: nová verze vedle V1 – V1 se kvůli popisu a kategorii neměnila.</summary>
    [Fact]
    public void ProductUpdatedV2_TvarJsonuSeNezmenil()
    {
        var updated = new ProductUpdatedV2(EventId, OccurredAtUtc, 7, "Mouse", "Wireless", 890m, 2, "Periferie",
            null, StockAvailabilityV1.LowStock, OccurredAtUtc);

        var json = OutboxMessageFactory.Create(updated).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","ProductId":7,"Name":"Mouse","Description":"Wireless","Price":890,"CategoryId":2,"CategoryName":"Periferie","ImageFileName":null,"Availability":"LowStock","AvailabilityChangedAtUtc":"2026-01-15T10:00:00Z"}""",
            json);
    }

    [Fact]
    public void StockAvailabilityChangedV1_TvarJsonuSeNezmenil()
    {
        var changed = new StockAvailabilityChangedV1(EventId, OccurredAtUtc, 7, StockAvailabilityV1.OutOfStock);

        var json = OutboxMessageFactory.Create(changed).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","ProductId":7,"Availability":"OutOfStock"}""",
            json);
    }

    /// <summary>E-shop krok 27A: doporučení „Zákazníci také koupili“ – pořadí ID je význam, čas výpočtu proti přepsání starším.</summary>
    [Fact]
    public void ProductRecommendationsChangedV1_TvarJsonuSeNezmenil()
    {
        var changed = new MiniShop.Contracts.Reporting.ProductRecommendationsChangedV1(EventId, OccurredAtUtc, 7, [12, 3], OccurredAtUtc);

        var json = OutboxMessageFactory.Create(changed).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","ProductId":7,"RecommendedProductIds":[12,3],"ComputedAtUtc":"2026-01-15T10:00:00Z"}""",
            json);
    }

    /// <summary>E-shop krok 28B: rezervace s balíky – sklad (kód a název) a položky.</summary>
    [Fact]
    public void StockReservedV2_TvarJsonuSeNezmenil()
    {
        var reserved = new StockReservedV2(EventId, OccurredAtUtc, 42, [new StockParcelV1("BRQ", "Brno", [new StockLineV1(7, 3)])]);

        var json = OutboxMessageFactory.Create(reserved).Payload;

        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"Parcels":[{"WarehouseCode":"BRQ","WarehouseName":"Brno","Lines":[{"ProductId":7,"Quantity":3}]}]}""",
            json);
    }

    /// <summary>E-shop krok 28C: zásilka za každý balík – příkaz, odpověď a „balík je na cestě“.</summary>
    [Fact]
    public void ZasilkaZaBalik_TvarJsonuSeNezmenil()
    {
        var command = new MiniShop.Contracts.Shipping.CreateShipmentV2(EventId, OccurredAtUtc, 42, 2, "Brno", "Jan Novak", "Dlouha 5",
            "Praha", "11000", 200m);
        var created = new MiniShop.Contracts.Shipping.ShipmentCreatedV2(EventId, OccurredAtUtc, 42, 2, "PPL", "CS1");
        var failed = new MiniShop.Contracts.Shipping.ShipmentFailedV2(EventId, OccurredAtUtc, 42, 2, "Nedorucujeme");
        var shipped = new ParcelShippedV1(EventId, OccurredAtUtc, 42, 2, 2, "Brno", "jan@example.com", "PPL", "CS1");

        Assert.Equal(
            """{"CommandId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ParcelNumber":2,"SenderWarehouse":"Brno","RecipientName":"Jan Novak","Street":"Dlouha 5","City":"Praha","PostalCode":"11000","CashOnDelivery":200}""",
            OutboxMessageFactory.Create(command).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ParcelNumber":2,"Carrier":"PPL","TrackingNumber":"CS1"}""",
            OutboxMessageFactory.Create(created).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ParcelNumber":2,"Reason":"Nedorucujeme"}""",
            OutboxMessageFactory.Create(failed).Payload);
        Assert.Equal(
            """{"EventId":"11111111-2222-3333-4444-555555555555","OccurredAtUtc":"2026-01-15T10:00:00Z","OrderId":42,"ParcelNumber":2,"ParcelCount":2,"WarehouseName":"Brno","CustomerEmail":"jan@example.com","Carrier":"PPL","TrackingNumber":"CS1"}""",
            OutboxMessageFactory.Create(shipped).Payload);
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
