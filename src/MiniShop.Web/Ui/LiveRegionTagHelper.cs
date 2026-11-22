using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MiniShop.Web.Ui;

/// <summary>
/// Vzhled krok F2: „živý výřez“ – část stránky, která se sama obnoví bez načtení celé stránky.
/// <c>&lt;div id="order-live" live-region="@Model.IsLive" live-topic="order:42"&gt;</c>: dokud se na něco čeká (<c>true</c>),
/// htmx na událost <see cref="RefreshEvent"/> stáhne na pozadí tutéž stránku, vybere z ní prvek se stejným <c>id</c>
/// a vymění ho (<c>hx-select</c>). Když server vykreslí výřez s <c>false</c> (hotovo), atributy chybí a obnovování skončí.
/// </summary>
/// <remarks>
/// Kdy obnovit, řídí <c>wwwroot/js/live-region.js</c>: vzhled krok F3a – s tématem (<c>live-topic</c>) a připojeným SignalR
/// hned na signál od serveru (pojistně jednou za 60 s – vzhled F3b), jinak (bez tématu, bez spojení) každé 3 s jako v F2.
/// Výřez musí mít <c>id</c>. Rozepsaný formulář ve výřezu se nepřepíše – live-region.js výměnu odloží.
/// </remarks>
[HtmlTargetElement(Attributes = AttributeName)]
public sealed class LiveRegionTagHelper : TagHelper
{
    public const string AttributeName = "live-region";

    /// <summary>Událost, na kterou htmx výřez obnoví (vyvolává ji live-region.js).</summary>
    public const string RefreshEvent = "live-refresh";

    /// <summary>Značka výřezu (i neaktivního) – podle ní ho najde live-region.js.</summary>
    public const string MarkerAttribute = "data-live-region";

    [HtmlAttributeName(AttributeName)]
    public bool IsLive { get; set; }

    /// <summary>Vzhled krok F3a: téma živých aktualizací (<c>LiveTopics</c>) – signál od serveru obnoví výřez hned.</summary>
    [HtmlAttributeName("live-topic")]
    public string? Topic { get; set; }

    /// <summary>
    /// Proud B2: o kolik milisekund po signálu obnovit. Pro výřez, jehož data počítá jiná služba ze stejné zprávy
    /// (Rychlost vyřízení – Reporting.Api čte proud po dávkách) – obnovení hned by často ukázalo ještě stará čísla.
    /// Víc signálů během čekání = jedno obnovení.
    /// </summary>
    [HtmlAttributeName("live-delay")]
    public int? DelayMs { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var id = output.Attributes["id"]?.Value?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException($"Živý výřez ({AttributeName}) potřebuje atribut id – podle něj ho htmx vybere z odpovědi.");
        }

        output.Attributes.SetAttribute(MarkerAttribute, string.Empty);
        if (!IsLive)
        {
            return;
        }

        var request = ViewContext.HttpContext.Request;
        output.Attributes.SetAttribute("hx-get", $"{request.PathBase}{request.Path}{request.QueryString}");
        output.Attributes.SetAttribute("hx-trigger", RefreshEvent);
        output.Attributes.SetAttribute("hx-select", $"#{id}");
        output.Attributes.SetAttribute("hx-swap", "outerHTML");
        if (!string.IsNullOrEmpty(Topic))
        {
            output.Attributes.SetAttribute("data-live-topic", Topic);
        }

        if (DelayMs is > 0)
        {
            output.Attributes.SetAttribute("data-live-delay", DelayMs.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
