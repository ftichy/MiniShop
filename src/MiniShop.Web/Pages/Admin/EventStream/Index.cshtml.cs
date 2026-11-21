using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.EventStream;

/// <summary>
/// Proud B1: <b>Proud zpráv</b> – co prošlo brokerem, přečtené z proudu <c>minishop.events</c> od zvoleného času.
/// Na rozdíl od stránky Zprávy (co čeká nebo selhalo) ukazuje historii: celou pouť objednávky napříč moduly.
/// Čtení proud nemění – stránku jde otevřít kolikrát chcete. Osobní údaje jsou zamaskované (<c>PersonalDataMask</c>).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOperations)]
public sealed class IndexModel(IEventStreamReader reader, TimeProvider timeProvider, ILogger<IndexModel> logger) : PageModel
{
    /// <summary>Volby „posledních N minut“ (0 = vlastní čas v <see cref="From"/>).</summary>
    public static readonly IReadOnlyList<(int Minutes, string Label)> Ranges =
        [(15, "15 minut"), (60, "1 hodina"), (360, "6 hodin"), (1440, "24 hodin"), (10080, "7 dní"), (0, "od zadaného času")];

    /// <summary>Typy zpráv pro filtr – všechny kontrakty.</summary>
    public static IReadOnlyList<string> Types { get; } = IntegrationMessageReader.ContractNames.Order(StringComparer.Ordinal).ToList();

    [BindProperty(SupportsGet = true, Name = "minutes")]
    public int Minutes { get; set; } = 60;

    /// <summary>Vlastní „od kdy“ v českém čase (formulář datetime-local).</summary>
    [BindProperty(SupportsGet = true, Name = "from")]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "type")]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true, Name = "order")]
    public int? OrderId { get; set; }

    public EventStreamResult? Result { get; private set; }

    public DateTime FromUtc { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        FromUtc = Minutes == 0 && From is { } local
            ? CzechTime.ToUtc(local)
            : now.AddMinutes(-(Minutes > 0 ? Minutes : 60));
        var type = Types.Contains(Type) ? Type : null;   // jen známé typy

        try
        {
            Result = await reader.ReadAsync(new EventStreamQuery(FromUtc, type, OrderId), cancellationToken);
        }
        catch (EventStreamUnavailableException ex)
        {
            logger.LogWarning("Proud zpráv nejde přečíst: {Error}", ex.Message);
            ErrorMessage = "Proud zpráv teď nejde přečíst (RabbitMQ neodpovídá, nebo proud po prvním nasazení ještě nevznikl – za chvíli to zkuste znovu).";
        }
    }
}
