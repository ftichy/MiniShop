using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Journeys;
using MiniShop.Domain.Exceptions;
using MiniShop.Infrastructure.Journeys;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Application;

/// <summary>
/// Krok 12A: zápis proudu událostí z formuláře objednávky a jeho projekce (časová osa, souhrn)
/// nad skutečnou SQLite databází v paměti. Každá akce běží ve vlastním scope jako HTTP požadavek.
/// </summary>
public sealed class JourneyServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDatabase _database = new();
    private readonly ServiceProvider _services;
    private readonly Guid _journeyId = Guid.NewGuid();

    public JourneyServiceTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddJourneysInfrastructure(_database.ConnectionString);
        services.AddScoped<IJourneyService, JourneyService>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<JourneysDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Prubeh_SlozeniCasoveOsyASouhrnu()
    {
        await Journeys(s => s.RecordAsync(_journeyId,
        [
            Event("FormOpened", 0),
            Event("FieldFocused", 1_000, "Input.CustomerName", "Jméno zákazníka"),
            Event("FieldChanged", 1_200, "Input.CustomerName", "Jméno zákazníka", "J"),
            Event("FieldChanged", 1_400, "Input.CustomerName", "Jméno zákazníka", "Ja"),
            Event("FieldChanged", 1_600, "Input.CustomerName", "Jméno zákazníka", "Jan"),
            Event("FieldBlurred", 3_000, "Input.CustomerName", "Jméno zákazníka"),
            Event("FieldFocused", 3_000, "Input.ProductId", "Produkt"),
            Event("FieldChanged", 4_000, "Input.ProductId", "Produkt", "2", "Bezdrátová myš – 890,00 Kč"),
            Event("FieldChanged", 5_000, "Input.ProductId", "Produkt", "1", "Mechanická klávesnice – 2 490,00 Kč"),
            Event("FieldBlurred", 6_000, "Input.ProductId", "Produkt"),
            Event("Clicked", 6_500, "Objednat", "Objednat"),
            Event("SubmitClicked", 6_500)
        ]));
        await Journeys(s => s.RecordOrderSubmittedAsync(_journeyId, 6_501, orderId: 7));

        var journey = await Journeys(s => s.GetByOrderIdAsync(7));

        Assert.Equal(_journeyId, journey.JourneyId);
        Assert.Equal(
        [
            "Otevřel formulář objednávky",
            "Vstoupil do pole „Jméno zákazníka“",
            "Vyplnil „Jméno zákazníka“: „Jan“",   // tři napsané znaky = jeden krok
            "Vstoupil do pole „Produkt“",
            "Vybral „Produkt“: Bezdrátová myš – 890,00 Kč",
            "Vybral „Produkt“: Mechanická klávesnice – 2 490,00 Kč",
            "Klikl na „Objednat“",
            "Odeslal formulář",
            "Objednávka č. 7 vznikla"
        ], journey.Steps.Select(s => s.Description));

        var typing = journey.Steps[2];
        Assert.Equal((1_200, 1_600), (typing.OffsetMs, typing.EndOffsetMs));

        Assert.Equal(6_501, journey.Summary.DurationMs);
        Assert.Equal(1, journey.Summary.ClickCount);
        Assert.Equal(0, journey.Summary.FailedSubmits);
        Assert.Equal(1, journey.Summary.ChangesByField["Jméno zákazníka"]);
        Assert.Equal(2, journey.Summary.ChangesByField["Produkt"]);
        Assert.Equal(
            [new JourneyFieldTimeDto("Produkt", 3_000), new JourneyFieldTimeDto("Jméno zákazníka", 2_000)],
            journey.Summary.TimeInFields);

        // Pro přehrání (krok 12B) jsou k dispozici všechny události včetně každého znaku.
        Assert.Equal(13, journey.Events.Count);

        // Začátek = čas příchodu na server minus čas od otevření formuláře (v testu stojí hodiny serveru → nejstarší odhad).
        Assert.Equal(Now.UtcDateTime.AddMilliseconds(-6_501), journey.StartedAtUtc);
    }

    [Fact]
    public async Task Prubeh_DavkyMimoPoradi_RadiSePodleCasuFormulare()
    {
        // Druhá dávka dorazila dřív (např. sendBeacon při odeslání předběhl pomalejší fetch).
        await Journeys(s => s.RecordAsync(_journeyId, [Event("SubmitClicked", 5_000)]));
        await Journeys(s => s.RecordAsync(_journeyId, [Event("FormOpened", 0), Event("Clicked", 2_000, "nav", "Produkty")]));
        await Journeys(s => s.RecordOrderSubmittedAsync(_journeyId, 5_001, orderId: 3));

        var journey = await Journeys(s => s.GetByOrderIdAsync(3));

        Assert.Equal(["FormOpened", "Clicked", "SubmitClicked", "OrderSubmitted"], journey.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task Zapis_OpakovanaDavka_NicNezdvoji()
    {
        IReadOnlyList<RecordJourneyEventRequest> batch = [Event("FormOpened", 0), Event("Clicked", 100, "a", "Odkaz")];

        await Journeys(s => s.RecordAsync(_journeyId, batch));
        await Journeys(s => s.RecordAsync(_journeyId, batch));                   // prohlížeč dávku poslal znovu
        await Journeys(s => s.RecordAsync(_journeyId, [batch[1], batch[1]]));   // a duplicita i uvnitř dávky

        Assert.Equal(2, await Db(db => db.JourneyEvents.CountAsync()));
    }

    [Fact]
    public async Task Zapis_NeuspesneOdeslani_JeVSouhrnu()
    {
        await Journeys(s => s.RecordAsync(_journeyId, [Event("FormOpened", 0), Event("SubmitClicked", 1_000)]));
        await Journeys(s => s.RecordValidationFailedAsync(_journeyId, 1_001, "Neplatný e-mail."));
        await Journeys(s => s.RecordAsync(_journeyId, [Event("SubmitClicked", 4_000)]));
        await Journeys(s => s.RecordOrderSubmittedAsync(_journeyId, 4_001, orderId: 5));

        var journey = await Journeys(s => s.GetByOrderIdAsync(5));

        Assert.Equal(1, journey.Summary.FailedSubmits);
        Assert.Contains("Server formulář vrátil: Neplatný e-mail.", journey.Steps.Select(s => s.Description));
    }

    [Fact]
    public async Task Zapis_ProhlizecNepustilOdeslani_JeVSouhrnu()
    {
        // Ověřeno v Chrome: „eva@“ v poli type=email → prohlížeč odeslání zastaví a server nic nedostane.
        await Journeys(s => s.RecordAsync(_journeyId,
        [
            Event("FormOpened", 0),
            Event("Clicked", 1_000, "order-submit", "Objednat"),
            Event("FieldInvalid", 1_001, "Input.CustomerName", "Jméno zákazníka", "Vyplňte toto pole."),
            Event("FieldInvalid", 1_002, "Input.CustomerEmail", "E-mail", "Zadejte část za znakem @."),   // stejný pokus
            Event("Clicked", 5_000, "order-submit", "Objednat"),
            Event("FieldInvalid", 5_001, "Input.CustomerEmail", "E-mail", "Zadejte část za znakem @."),   // další pokus
            Event("SubmitClicked", 9_000)
        ]));
        await Journeys(s => s.RecordOrderSubmittedAsync(_journeyId, 9_001, orderId: 11));

        var journey = await Journeys(s => s.GetByOrderIdAsync(11));

        Assert.Equal(2, journey.Summary.FailedSubmits);
        Assert.Contains("Prohlížeč nepustil odeslání – „E-mail“: Zadejte část za znakem @.", journey.Steps.Select(s => s.Description));
    }

    [Theory]
    [InlineData("OrderSubmitted")]     // číslo objednávky smí zapsat jen server
    [InlineData("ValidationFailed")]
    [InlineData("Neznámý")]
    [InlineData("fieldchanged")]       // velikost písmen se musí shodovat
    public async Task Zapis_TypKteryProhlizecNesmiPoslat_JeChyba(string type)
    {
        await Assert.ThrowsAsync<DomainException>(() => Journeys(s => s.RecordAsync(_journeyId, [Event(type, 0)])));
        Assert.Equal(0, await Db(db => db.JourneyEvents.CountAsync()));
    }

    [Fact]
    public async Task Zapis_PrilisVelkaDavka_JeChyba()
    {
        var batch = Enumerable.Range(0, JourneyService.MaxBatchSize + 1).Select(i => Event("Clicked", i, "a", "Odkaz")).ToList();

        await Assert.ThrowsAsync<DomainException>(() => Journeys(s => s.RecordAsync(_journeyId, batch)));
    }

    [Fact]
    public async Task Prehrani_StejnyProudJakoPrubeh()
    {
        await Journeys(s => s.RecordAsync(_journeyId,
        [
            Event("FormOpened", 0),
            Event("FieldChanged", 1_000, "Input.CustomerName", "Jméno zákazníka", "J"),
            Event("FieldChanged", 1_300, "Input.CustomerName", "Jméno zákazníka", "Jo"),
            Event("SubmitClicked", 60_000)                  // minuta přemýšlení
        ]));
        await Journeys(s => s.RecordOrderSubmittedAsync(_journeyId, 60_001, orderId: 9));

        var replay = await Journeys(s => s.GetReplayByOrderIdAsync(9));

        // Přehrávač dostane všechny události (i každý znak) a minuta čekání se zkrátí na 3 s.
        Assert.Equal(replay.Journey.Events, replay.Frames.Select(f => f.Event));
        Assert.Equal([0L, 1_000, 1_300, 4_300, 4_301], replay.Frames.Select(f => f.PlayAtMs));
        Assert.Equal(60_001, replay.Journey.Summary.DurationMs);
    }

    [Fact]
    public async Task Prubeh_ObjednavkaBezProudu_NotFound()
    {
        await Journeys(s => s.RecordAsync(_journeyId, [Event("FormOpened", 0)]));   // proud bez objednávky

        await Assert.ThrowsAsync<NotFoundException>(() => Journeys(s => s.GetByOrderIdAsync(1)));
    }

    // ---------- Pomocné metody ----------

    private static RecordJourneyEventRequest Event(string type, long offsetMs, string? target = null, string? label = null,
        string? value = null, string? valueText = null) =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = type,
            OffsetMs = offsetMs,
            Target = target,
            Label = label,
            Value = value,
            ValueText = valueText
        };

    private Task Journeys(Func<IJourneyService, Task> action) => Journeys(async s =>
    {
        await action(s);
        return true;
    });

    private async Task<T> Journeys<T>(Func<IJourneyService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IJourneyService>());
    }

    private async Task<T> Db<T>(Func<JourneysDbContext, Task<T>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<JourneysDbContext>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }
}
