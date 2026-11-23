namespace MiniShop.Domain.Inventory;

/// <summary>
/// E-shop krok 28A (směr C): sklad, ze kterého se zboží vydává. Dnes Praha (výchozí – sem patří všechny zásoby z doby
/// před sklady) a Brno. Sklady zakládá migrace; správa skladů v aplikaci zatím není.
/// </summary>
public sealed class Warehouse
{
    /// <summary>Praha – výchozí sklad (dnešní zásoby, naskladnění bez volby skladu, rezervace ze starší verze kódu).</summary>
    public const int DefaultId = 1;

    /// <summary>Brno – druhý sklad, začíná prázdný.</summary>
    public const int BrnoId = 2;

    // Konstruktor pro EF Core.
    private Warehouse()
    {
    }

    public Warehouse(int id, string code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    public int Id { get; private set; }

    /// <summary>Krátký kód (PRG, BRQ) – pro štítky v administraci.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
}

/// <summary>E-shop krok 28A: kolik kusů produktu je v jednom skladu (část agregátu <see cref="Product"/>).</summary>
public sealed class WarehouseStock
{
    // Konstruktor pro EF Core.
    private WarehouseStock()
    {
    }

    internal WarehouseStock(int warehouseId, int quantity)
    {
        WarehouseId = warehouseId;
        Quantity = quantity;
    }

    public int WarehouseId { get; private set; }

    public int Quantity { get; internal set; }
}
