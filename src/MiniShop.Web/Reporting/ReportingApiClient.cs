using MiniShop.Application.Reporting;

namespace MiniShop.Web.Reporting;

/// <summary>
/// Mikroservisy M1: přehled tržeb ze služby MiniShop.Reporting.Api. Stránka /Admin/Sales zná jen toto rozhraní –
/// testy Webu ho nahradí, aniž by služba běžela.
/// </summary>
public interface ISalesReportApi
{
    /// <exception cref="SalesReportUnavailableException">Služba neběží nebo odpověděla chybou.</exception>
    Task<SalesReportDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Smazat čtecí model a nechat ho postavit znovu ze snímků objednávek (výsledek za pár sekund).</summary>
    /// <exception cref="SalesReportUnavailableException">Služba neběží nebo odpověděla chybou.</exception>
    Task RebuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Proud B2: přehled Rychlost vyřízení ze služby Reporting.Api (data staví čtenář proudu minishop.events).
/// </summary>
public interface ILeadTimeApi
{
    /// <exception cref="SalesReportUnavailableException">Služba neběží nebo odpověděla chybou.</exception>
    Task<LeadTimeReportDto> GetLeadTimesAsync(CancellationToken cancellationToken = default);

    /// <summary>Přestavět z proudu – smazat data a záložku přetočit na začátek (výsledek za pár sekund).</summary>
    /// <exception cref="SalesReportUnavailableException">Služba neběží nebo odpověděla chybou.</exception>
    Task RebuildLeadTimesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Služba přehledu tržeb je nedostupná – stránka to řekne lidsky, zbytek e-shopu běží dál.</summary>
public sealed class SalesReportUnavailableException(Exception inner)
    : Exception("Služba přehledu tržeb (reporting-api) je nedostupná.", inner);

/// <summary>
/// Mikroservisy M1: HTTP klient služby přehledu tržeb. Servisní klíč přidá <c>ServiceApiKeyHandler</c>, opakování
/// a circuit breaker ServiceDefaults (po pár neúspěších odpovídá hned, nečeká na timeout).
/// </summary>
public sealed class ReportingApiClient(HttpClient http) : ISalesReportApi, ILeadTimeApi
{
    private const string Sales = "api/sales";
    private const string LeadTimes = "api/lead-times";   // proud B2

    public async Task<LeadTimeReportDto> GetLeadTimesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.GetFromJsonAsync<LeadTimeReportDto>(LeadTimes, cancellationToken)
                ?? throw new InvalidOperationException("Prázdná odpověď služby přehledu (Rychlost vyřízení).");
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new SalesReportUnavailableException(ex);
        }
    }

    public async Task RebuildLeadTimesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsync($"{LeadTimes}/rebuild", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new SalesReportUnavailableException(ex);
        }
    }

    public async Task<SalesReportDto> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await http.GetFromJsonAsync<SalesReportDto>(Sales, cancellationToken)
                ?? throw new InvalidOperationException("Prázdná odpověď služby přehledu tržeb.");
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new SalesReportUnavailableException(ex);
        }
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsync($"{Sales}/rebuild", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            throw new SalesReportUnavailableException(ex);
        }
    }

    /// <summary>Chyba sítě, chybový status, timeout nebo otevřený circuit breaker – ne zrušení požadavku uživatelem.</summary>
    private static bool IsUnavailable(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && ex is not SalesReportUnavailableException;
}
