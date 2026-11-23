namespace MiniShop.Domain.Inventory;

/// <summary>Jedna část rezervace: kolik kusů produktu se vezme z kterého skladu.</summary>
public sealed record StockAllocation(int WarehouseId, int ProductId, int Quantity);

/// <summary>
/// E-shop krok 28A (směr C): <b>přidělení zásob</b> – odkud se zboží objednávky vezme. Rozhoduje sklad, protože zná zásoby;
/// Ordering se dozví jen výsledek (od kroku 28B jako balíky).
/// </summary>
/// <remarks>
/// Pravidlo (rozhodnutí 22. 11. 2026): <b>jeden sklad, když to jde</b> – méně balíků, levnější doprava. Jinak co nejméně
/// skladů: postupně bere sklad, který pokryje nejvíc zbývajících kusů (při shodě nižší ID), a řádek se smí rozdělit.
/// Přirovnání: skladník nejdřív hledá regál, kde je všechno; když takový není, začne tím nejplnějším.
/// </remarks>
public static class StockAllocator
{
    /// <param name="lines">Produkt (s načtenou zásobou po skladech) a požadované množství; jeden produkt jednou.</param>
    /// <returns>Části rezervace, nebo null, když zboží nestačí ani ve všech skladech dohromady.</returns>
    public static IReadOnlyList<StockAllocation>? Allocate(IReadOnlyList<(Product Product, int Quantity)> lines)
    {
        if (lines.Any(line => line.Product.StockQuantity < line.Quantity))
        {
            return null;
        }

        var warehouseIds = lines.SelectMany(line => line.Product.Stocks.Select(s => s.WarehouseId))
            .Append(Warehouse.DefaultId).Distinct().Order().ToList();

        // 1. Jeden sklad, který má všechno.
        var single = warehouseIds.FirstOrDefault(id => lines.All(line => line.Product.StockIn(id) >= line.Quantity));
        if (single != 0)
        {
            return [.. lines.Select(line => new StockAllocation(single, line.Product.Id, line.Quantity))];
        }

        // 2. Co nejméně skladů – vždy ten, který pokryje nejvíc zbývajících kusů.
        // Podle pořadí řádku (ne ID produktu) – jeden produkt je v objednávce jednou, volající řádky slučuje.
        var remaining = Enumerable.Range(0, lines.Count).ToDictionary(index => index, index => lines[index].Quantity);
        var allocations = new List<StockAllocation>();
        var unused = warehouseIds.ToList();
        while (remaining.Values.Any(quantity => quantity > 0) && unused.Count > 0)
        {
            var best = unused
                .OrderByDescending(id => remaining.Sum(r => Math.Min(r.Value, lines[r.Key].Product.StockIn(id))))
                .ThenBy(id => id)
                .First();
            unused.Remove(best);
            foreach (var (index, quantity) in remaining.Where(r => r.Value > 0).ToList())
            {
                var take = Math.Min(quantity, lines[index].Product.StockIn(best));
                if (take > 0)
                {
                    allocations.Add(new StockAllocation(best, lines[index].Product.Id, take));
                    remaining[index] = quantity - take;
                }
            }
        }

        return remaining.Values.Any(quantity => quantity > 0) ? null : allocations;
    }
}
