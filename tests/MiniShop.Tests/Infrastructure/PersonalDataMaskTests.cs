using System.Text.Json;
using MiniShop.Contracts;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Infrastructure.Integration;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Proud B1: stránka Proud zpráv ukazuje obsah zpráv – osobní údaje musí být zamaskované. Hlídá i dnešní kontrakty:
/// nové pole s osobním údajem = doplnit do <see cref="PersonalDataMask.Fields"/>.
/// </summary>
public sealed class PersonalDataMaskTests
{
    [Fact]
    public void ObjednavkaSeSnimkem_JmenoEmailAdresaZamaskovane_ProduktyAIdZustanou()
    {
        var placed = new OrderPlacedV2(Guid.NewGuid(), DateTime.UtcNow, 42, "ucet-1", "Jan Novák", "jan@example.com",
            [new OrderItemV2(7, "Herní myš", 1, 990m, 0m, 990m)], "Courier", 99m, "Dlouhá 5, 110 00 Praha", 1089m);

        var masked = PersonalDataMask.Mask(JsonSerializer.Serialize(placed));

        Assert.DoesNotContain("Jan Novák", masked);
        Assert.DoesNotContain("jan@example.com", masked);
        Assert.DoesNotContain("Dlouhá", masked);
        Assert.Contains("Herní myš", masked);   // název produktu není osobní údaj
        Assert.Contains("\"OrderId\": 42", masked);
        Assert.Contains("ucet-1", masked);        // ID účtu (jako enduser.id v trasách)
    }

    [Fact]
    public void PrikazDopravci_AdresaAJmenoPrijemceZamaskovane()
    {
        var command = new CreateShipmentV1(Guid.NewGuid(), DateTime.UtcNow, 42, "Jan Novák", "Dlouhá 5", "Praha", "11000", 990m);

        var masked = PersonalDataMask.Mask(JsonSerializer.Serialize(command));

        Assert.DoesNotContain("Jan Novák", masked);
        Assert.DoesNotContain("Dlouhá 5", masked);
        Assert.DoesNotContain("Praha", masked);
        Assert.DoesNotContain("11000", masked);
        Assert.Contains("990", masked);
    }

    [Fact]
    public void VolnyTextZakaznika_DuvodVratky_Zamaskovany() =>
        Assert.DoesNotContain("rozbité", PersonalDataMask.Mask(JsonSerializer.Serialize(
            new ReturnRequestedV1(Guid.NewGuid(), DateTime.UtcNow, 42, "jan@example.com", "Přišlo rozbité, volejte 777 123 456", DateTime.UtcNow))));

    /// <summary>
    /// Pojistka: žádný dnešní kontrakt nemá pole, které podle jména vypadá jako osobní údaj, a maska ho nezná
    /// (Name, Address, Phone…). Nový kontrakt s takovým polem tu spadne – doplnit <see cref="PersonalDataMask.Fields"/>.
    /// </summary>
    [Fact]
    public void KazdePoleSOsobnimUdajem_MaskaZna()
    {
        string[] suspicious = ["Name", "Email", "Address", "Phone", "Street", "City", "PostalCode", "Reason"];
        string[] allowed = ["ProductName", "Name", "CategoryName", "ImageFileName", "Carrier"];   // produkty a kategorie – nejsou osobní údaje

        var unknown = typeof(IIntegrationMessage).Assembly.GetTypes()
            .Where(t => typeof(IIntegrationMessage).IsAssignableFrom(t) || t.Namespace?.StartsWith("MiniShop.Contracts", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties().Select(p => p.Name))
            .Distinct()
            .Where(name => suspicious.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Where(name => !allowed.Contains(name) && !PersonalDataMask.Fields.Contains(name) && !name.EndsWith("Email", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(unknown);
    }
}
