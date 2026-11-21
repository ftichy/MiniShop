using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Proud B1: zamaskuje osobní údaje v JSONu zprávy, než ho ukáže stránka (Proud zpráv). Zprávy mezi moduly osobní údaje
/// nesou (jméno a e-mail zákazníka kvůli e-mailům, adresa pro dopravce) – zaměstnanec v Provozu je k práci nepotřebuje.
/// </summary>
/// <remarks>
/// Podle jména pole (kdekoli ve stromu, i v položkách). <b>Nové pole s osobním údajem v kontraktu = doplnit sem</b>
/// (hlídá <c>PersonalDataMaskTests</c> pro dnešní kontrakty). Názvy produktů, ID a částky zůstávají.
/// </remarks>
public static class PersonalDataMask
{
    public const string Masked = "•••";

    /// <summary>Pole s osobními údaji (a volným textem zákazníka – důvod vratky může obsahovat cokoli).</summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CustomerName", "CustomerEmail", "RecipientName", "DeliveryAddress", "Street", "City", "PostalCode", "Reason",
        "Email", "Phone"
    };

    public static string Mask(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return "(nečitelný JSON)";
        }

        MaskNode(root);
        return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
               ?? "null";
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsPersonal(name) && value is not null)
                    {
                        obj[name] = Masked;
                    }
                    else
                    {
                        MaskNode(value);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    MaskNode(item);
                }

                break;
        }
    }

    private static bool IsPersonal(string name) =>
        Fields.Contains(name) || name.EndsWith("Email", StringComparison.OrdinalIgnoreCase);
}
