using System.Net;
using System.Text.RegularExpressions;

namespace MiniShop.TrafficGenerator;

/// <summary>Nejmenší možné čtení HTML: antiforgery token a skryté pole formuláře (stránky jsou generované Razorem, ne cizí web).</summary>
public static partial class HtmlForms
{
    public static string? AntiForgeryToken(string html) => HiddenValues(html, "__RequestVerificationToken").FirstOrDefault();

    public static IEnumerable<string> HiddenValues(string html, string name)
    {
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            if (Attribute(tag, "name") == name && Attribute(tag, "value") is { } value)
            {
                yield return WebUtility.HtmlDecode(value);
            }
        }
    }

    /// <summary>Hodnoty z <c>name="productId" value="…"</c> v tlačítkách „Do košíku“ (první se vrací jako první).</summary>
    public static IReadOnlyList<int> ProductIds(string html) =>
        [.. HiddenValues(html, "productId").Select(int.Parse).Distinct()];

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\b{name}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}

public static partial class HtmlForms
{
    /// <summary>Všechna skrytá pole stránky kromě antiforgery tokenu (u pokladny: verze košíku a ID proudu Journeys).</summary>
    public static IEnumerable<(string Name, string Value)> HiddenInputs(string html)
    {
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            var type = Regex.Match(tag, "\\btype=\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var name = Regex.Match(tag, "\\bname=\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var value = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (type.Success && type.Groups[1].Value == "hidden" && name.Success && value.Success
                && name.Groups[1].Value != "__RequestVerificationToken")
            {
                yield return (name.Groups[1].Value, WebUtility.HtmlDecode(value.Groups[1].Value));
            }
        }
    }
}
