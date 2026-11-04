using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Messages;

/// <summary>
/// EDA krok 13: provoz zpráv pro admina – co čeká v outboxech modulů, které zprávy vyčerpaly pokusy (poison) a co leží
/// v dead-letter frontách RabbitMQ. Obojí jde po opravě příčiny vrátit ke zpracování.
/// </summary>
/// <remarks>
/// Ukazuje moduly služby Web. Sklad (Inventory.Api) má vlastní databázi i frontu – jeho stav hlásí /health/inventory.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(
    IEnumerable<IMessageStore> stores,
    RabbitMqDeadLetterService deadLetters,
    ILogger<IndexModel> logger) : PageModel
{
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(3);

    public IReadOnlyList<MessageStoreStatus> Modules { get; private set; } = [];

    /// <summary>Počty v dead-letter frontách; null = RabbitMQ teď nejde zkontrolovat.</summary>
    public IReadOnlyDictionary<string, uint>? DeadLetters { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<MessageStoreStatus>();
        foreach (var store in stores.OrderBy(s => s.ModuleName, StringComparer.Ordinal))
        {
            statuses.Add(await store.GetStatusAsync(cancellationToken));
        }

        Modules = statuses;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrokerTimeout);
            DeadLetters = await deadLetters.GetCountsAsync(timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Zprávy] Dead-letter fronty nejde zkontrolovat: {Error}", ex.Message);
        }
    }

    /// <summary>Poison zpráva z outboxu dostane nové pokusy (po opravě příčiny – např. chybějící data, výpadek SMTP).</summary>
    public async Task<IActionResult> OnPostRetryAsync(string module, Guid id, CancellationToken cancellationToken)
    {
        var store = stores.FirstOrDefault(s => s.ModuleName == module);
        StatusMessage = store is not null && await store.RetryAsync(id, cancellationToken)
            ? $"Zpráva se v modulu {module} zkusí znovu (za pár sekund)."
            : "Zpráva už nečeká – mezitím se zpracovala nebo neexistuje.";
        return RedirectToPage();
    }

    /// <summary>Zprávy z dead-letter fronty zpět do fronty modulu (s vynulovaným počtem pokusů).</summary>
    public async Task<IActionResult> OnPostRequeueAsync(string queue, CancellationToken cancellationToken)
    {
        try
        {
            var moved = await deadLetters.RequeueAsync(queue, cancellationToken);
            StatusMessage = $"Do fronty {queue} vráceno {moved} zpráv.";
        }
        catch (Exception ex) when (ex is ArgumentException || !cancellationToken.IsCancellationRequested)
        {
            StatusMessage = $"Vrácení se nepodařilo: {ex.Message}";
        }

        return RedirectToPage();
    }
}
