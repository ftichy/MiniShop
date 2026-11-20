using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MiniShop.Web.Ui;

/// <summary>
/// Vzhled krok F2: „živý výřez“ – část stránky, která se sama obnoví bez načtení celé stránky.
/// <c>&lt;div id="order-live" live-region="@Model.IsLive"&gt;</c>: dokud se na něco čeká (<c>true</c>), htmx každé
/// <see cref="Interval"/> stáhne na pozadí tutéž stránku, vybere z ní prvek se stejným <c>id</c> a vymění ho
/// (<c>hx-select</c>). Když server vykreslí výřez s <c>false</c> (hotovo), atributy chybí a dotazování samo skončí.
/// </summary>
/// <remarks>
/// Výřez musí mít <c>id</c>. Rozepsaný formulář ve výřezu se nepřepíše – <c>wwwroot/js/live-region.js</c> výměnu odloží.
/// Nahrazuje celé obnovení stránky (<c>data-reload-when-idle</c> + <c>orders-reload.js</c> do kroku F2).
/// </remarks>
[HtmlTargetElement(Attributes = AttributeName)]
public sealed class LiveRegionTagHelper : TagHelper
{
    public const string AttributeName = "live-region";

    /// <summary>Jak často se ptát (sága trvá ~5–8 s, krok dopravce 30 s).</summary>
    public const string Interval = "3s";

    /// <summary>Značka výřezu (i neaktivního) – podle ní ho najde live-region.js.</summary>
    public const string MarkerAttribute = "data-live-region";

    [HtmlAttributeName(AttributeName)]
    public bool IsLive { get; set; }

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
        output.Attributes.SetAttribute("hx-trigger", $"every {Interval}");
        output.Attributes.SetAttribute("hx-select", $"#{id}");
        output.Attributes.SetAttribute("hx-swap", "outerHTML");
    }
}
