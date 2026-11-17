using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Infrastructure.Hosting;
using MiniShop.Web.Accounts;
using MiniShop.Web.Messages;

namespace MiniShop.Web.Pages.Admin.Messages;

/// <summary>
/// EDA krok 13: provoz zpráv pro admina – co čeká v outboxech modulů, které zprávy vyčerpaly pokusy (poison) a co leží
/// v dead-letter frontách RabbitMQ. Obojí jde po opravě příčiny vrátit ke zpracování.
/// </summary>
/// <remarks>
/// Mikroservisy M2B: všechny služby – Web v procesu, sklad a přehled tržeb přes jejich <c>/api/messages</c>
/// (<see cref="IServiceMessages"/>). Nedostupná služba se ukáže jako „nedostupná“, ostatní normálně.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireOperations)]   // účty krok 12B
public sealed class IndexModel(IEnumerable<IServiceMessages> services, ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<ServiceSection> Services { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Služby se ptají souběžně – pomalá (nedostupná) nezdrží ostatní víc, než je nutné.
        Services = await Task.WhenAll(services.Select(service => LoadAsync(service, cancellationToken)));
    }

    /// <summary>Poison zpráva z outboxu dostane nové pokusy (po opravě příčiny – např. chybějící data, výpadek SMTP).</summary>
    public async Task<IActionResult> OnPostRetryAsync(string service, string module, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            StatusMessage = Find(service) is { } target && await target.RetryAsync(module, id, cancellationToken)
                ? $"Zpráva se v modulu {module} zkusí znovu (za pár sekund)."
                : "Zpráva už nečeká – mezitím se zpracovala nebo neexistuje.";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Zprávy] Opakování ve službě {Service} se nepodařilo: {Error}", service, ex.Message);
            StatusMessage = $"Chyba: služba {service} teď neodpovídá – zkuste to za chvíli.";
        }

        return RedirectToPage();
    }

    /// <summary>Zprávy z dead-letter fronty zpět do fronty modulu (s vynulovaným počtem pokusů).</summary>
    public async Task<IActionResult> OnPostRequeueAsync(string service, string queue, CancellationToken cancellationToken)
    {
        try
        {
            var target = Find(service) ?? throw new ArgumentException($"Služba {service} neexistuje.", nameof(service));
            var moved = await target.RequeueAsync(queue, cancellationToken);
            StatusMessage = $"Do fronty {queue} vráceno {moved} zpráv.";
        }
        catch (Exception ex) when (ex is ArgumentException || !cancellationToken.IsCancellationRequested)
        {
            StatusMessage = $"Chyba: vrácení se nepodařilo – {ex.Message}";
        }

        return RedirectToPage();
    }

    private IServiceMessages? Find(string service) => services.FirstOrDefault(s => s.ServiceName == service);

    private async Task<ServiceSection> LoadAsync(IServiceMessages service, CancellationToken cancellationToken)
    {
        try
        {
            return new ServiceSection(service.ServiceName, service.DisplayName, await service.GetAsync(cancellationToken));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Zprávy] Služba {Service} neodpovídá: {Error}", service.ServiceName, ex.Message);
            return new ServiceSection(service.ServiceName, service.DisplayName, null);
        }
    }
}

/// <param name="Status">Null = služba teď neodpovídá.</param>
public sealed record ServiceSection(string ServiceName, string DisplayName, ServiceMessagesStatus? Status);
