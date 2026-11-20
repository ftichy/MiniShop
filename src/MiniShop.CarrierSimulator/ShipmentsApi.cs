using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MiniShop.CarrierSimulator;

/// <summary>
/// REST API dopravce (verze v1). Tvar je záměrně „cizí“ (camelCase, stavy snake_case, chyby jako { code, message }) –
/// MiniShop si ho v kroku 26B přeloží na své pojmy. Celé <c>/v1</c> jen s hlavičkou <c>X-Api-Key</c>.
/// </summary>
public static partial class ShipmentsApi
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    public static void MapShipmentsApi(this WebApplication app)
    {
        var v1 = app.MapGroup("/v1").AddEndpointFilter(RequireApiKey);
        v1.MapPost("/shipments", CreateAsync);
        v1.MapGet("/shipments/{trackingNumber}", Get);
    }

    private static async ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<CarrierOptions>>().Value.ApiKey;
        var sent = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(expected))
            ? await next(context)
            : Error(StatusCodes.Status401Unauthorized, "unauthorized", "Chybí nebo nesedí X-Api-Key.");
    }

    private static IResult CreateAsync(
        HttpContext http, CreateShipmentRequest? request, ShipmentStore store, ChaosDice dice,
        IOptions<CarrierOptions> options, TimeProvider time, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("MiniShop.CarrierSimulator.Shipments");
        var chaos = options.Value.Chaos;

        // Zlobení 1: dopravce je přetížený a nic neudělá – e-shop musí zopakovat (se stejným Idempotency-Key).
        if (dice.Roll(chaos.FailureRate))
        {
            log.LogInformation("Zlobení: 503 bez vytvoření zásilky");
            return Error(StatusCodes.Status503ServiceUnavailable, "service_unavailable", "Dopravce je přetížený, zkuste to znovu.");
        }

        var idempotencyKey = http.Request.Headers[IdempotencyKeyHeader].ToString();
        if (idempotencyKey.Length is 0 or > 100)
        {
            return Error(StatusCodes.Status400BadRequest, "idempotency_key_required", "Hlavička Idempotency-Key je povinná (max. 100 znaků).");
        }

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, HashJson)));
        var now = time.GetUtcNow().UtcDateTime;

        // Zlobení 3 (krok 26B): na adresu nedoručujeme – trvalá chyba (opakování nepomůže). Jen u nového klíče:
        // zásilka, která už vznikla, se při opakování neodmítne.
        var known = store.Read(state => state.Shipments.Any(s => s.IdempotencyKey == idempotencyKey));
        if (!known && dice.Roll(chaos.RejectRate))
        {
            log.LogInformation("Zlobení: zásilka pro {Reference} odmítnuta – adresa mimo oblast", request!.Reference);
            return Error(StatusCodes.Status422UnprocessableEntity, "address_not_serviceable", "Na tuto adresu nedoručujeme.");
        }

        var (shipment, replayed, conflict) = store.Update(state =>
        {
            var existing = state.Shipments.FirstOrDefault(s => s.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                return (ToResponse(existing), true, existing.RequestHash != hash);
            }

            var created = new Shipment
            {
                TrackingNumber = NewTrackingNumber(state, dice),
                Reference = request!.Reference!.Trim(),
                Recipient = new Recipient
                {
                    Name = request.Recipient!.Name!.Trim(),
                    Street = request.Recipient.Street!.Trim(),
                    City = request.Recipient.City!.Trim(),
                    PostalCode = request.Recipient.PostalCode!.Replace(" ", "")
                },
                WeightGrams = request.WeightGrams,
                CashOnDelivery = request.CashOnDelivery,
                CreatedAtUtc = now,
                IdempotencyKey = idempotencyKey,
                RequestHash = hash,
                // Zlobení 4 (krok 26D): příjemce nebude k zastižení → zásilka se vrátí odesílateli.
                Undeliverable = dice.Roll(chaos.UndeliverableRate)
            };
            if (created.Undeliverable)
            {
                log.LogInformation("Zlobení: zásilka {TrackingNumber} se nepodaří doručit", created.TrackingNumber);
            }
            state.Shipments.Add(created);
            return (ToResponse(created), false, false);
        });

        if (conflict)
        {
            return Error(StatusCodes.Status409Conflict, "idempotency_key_reused",
                "Idempotency-Key už byl použit pro jinou zásilku (jiný obsah požadavku).");
        }

        // Do logu jen číslo zásilky a reference – adresa příjemce je osobní údaj.
        log.LogInformation("Zásilka {TrackingNumber} pro {Reference} {Outcome}", shipment.TrackingNumber, shipment.Reference,
            replayed ? "už existuje (opakovaný požadavek)" : "vytvořena");

        // Zlobení 2: zásilka vznikla, ale odpověď „se ztratí“. E-shop neví, jestli vznikla – opakování se stejným
        // Idempotency-Key ji najde (žádná druhá zásilka, žádné druhé číslo).
        if (!replayed && dice.Roll(chaos.LostResponseRate))
        {
            log.LogInformation("Zlobení: zásilka {TrackingNumber} vznikla, ale odpověď 503", shipment.TrackingNumber);
            return Error(StatusCodes.Status503ServiceUnavailable, "service_unavailable", "Dopravce je přetížený, zkuste to znovu.");
        }

        if (replayed)
        {
            http.Response.Headers[ReplayedHeader] = "true";
            return Results.Ok(shipment);
        }

        return Results.Created($"/v1/shipments/{shipment.TrackingNumber}", shipment);
    }

    private static IResult Get(string trackingNumber, ShipmentStore store)
    {
        var shipment = store.Read(state =>
        {
            var found = state.Shipments.FirstOrDefault(s => s.TrackingNumber == trackingNumber);
            return found is null ? null : ToResponse(found);
        });

        return shipment is null
            ? Error(StatusCodes.Status404NotFound, "not_found", "Zásilka s tímto číslem neexistuje.")
            : Results.Ok(shipment);
    }

    private static IResult? Validate(CreateShipmentRequest? request)
    {
        if (request is null)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "invalid_request", "Chybí tělo požadavku.");
        }

        string? missing =
            string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 64 ? "reference" :
            request.Recipient is null ? "recipient" :
            string.IsNullOrWhiteSpace(request.Recipient.Name) || request.Recipient.Name.Length > 100 ? "recipient.name" :
            string.IsNullOrWhiteSpace(request.Recipient.Street) || request.Recipient.Street.Length > 200 ? "recipient.street" :
            string.IsNullOrWhiteSpace(request.Recipient.City) || request.Recipient.City.Length > 100 ? "recipient.city" :
            null;
        if (missing is not null)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "invalid_request", $"Chybí nebo je příliš dlouhé pole {missing}.");
        }

        // Trvalé chyby: opakování nepomůže, e-shop musí zásilku opravit (krok 26B → ShipmentFailedV1).
        if (request.Recipient!.PostalCode is null || !PostalCode().IsMatch(request.Recipient.PostalCode))
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "invalid_postal_code", "PSČ musí mít 5 číslic (např. 110 00).");
        }

        if (request.WeightGrams is <= 0 or > 31_500)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "parcel_too_heavy", "Hmotnost musí být 1 g až 31,5 kg.");
        }

        if (request.CashOnDelivery is < 0)
        {
            return Error(StatusCodes.Status422UnprocessableEntity, "invalid_request", "Dobírka nesmí být záporná.");
        }

        return null;
    }

    private static string NewTrackingNumber(CarrierState state, ChaosDice dice)
    {
        string number;
        do
        {
            number = "CS" + dice.Digits(10);
        }
        while (state.Shipments.Any(s => s.TrackingNumber == number));

        return number;
    }

    private static ShipmentResponse ToResponse(Shipment s) => new(
        s.TrackingNumber, s.Reference, s.Status, s.CreatedAtUtc,
        s.Events.Select(e => new ShipmentEventResponse(e.Id, e.Sequence, e.Status, e.OccurredAtUtc)).ToList());

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorResponse(code, message), statusCode: status);

    [GeneratedRegex(@"^\d{3} ?\d{2}$")]
    private static partial Regex PostalCode();
}

public sealed record CreateShipmentRequest(
    string? Reference, RecipientRequest? Recipient, int? WeightGrams = null, decimal? CashOnDelivery = null);

public sealed record RecipientRequest(string? Name, string? Street, string? City, string? PostalCode);

public sealed record ShipmentResponse(
    string TrackingNumber, string Reference, string Status, DateTime CreatedAt, IReadOnlyList<ShipmentEventResponse> Events);

public sealed record ShipmentEventResponse(string Id, int Sequence, string Status, DateTime OccurredAt);

public sealed record ErrorResponse(string Code, string Message);
