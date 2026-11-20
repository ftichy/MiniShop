using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MiniShop.CarrierSimulator;

/// <summary>
/// Zásilky v paměti + soubor <c>carrier-state.json</c> (zápis přes dočasný soubor a přejmenování – pád uprostřed zápisu
/// nenechá rozbitý soubor). Simulátor běží v jedné kopii, takže stačí zámek v procesu. Každá změna = celý soubor znovu;
/// pro desítky zásilek denně je to zanedbatelné.
/// </summary>
public sealed class ShipmentStore
{
    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly Lock _lock = new();
    private readonly string _path;
    private readonly CarrierState _state;

    public ShipmentStore(IOptions<CarrierOptions> options)
    {
        Directory.CreateDirectory(options.Value.DataPath);
        _path = Path.Combine(options.Value.DataPath, "carrier-state.json");
        _state = File.Exists(_path)
            ? JsonSerializer.Deserialize<CarrierState>(File.ReadAllText(_path), FileJson) ?? new CarrierState()
            : new CarrierState();
    }

    /// <summary>Přečte stav pod zámkem (výsledek nesmí držet odkazy na vnitřní objekty – vracet kopie/hodnoty).</summary>
    public T Read<T>(Func<CarrierState, T> read)
    {
        lock (_lock)
        {
            return read(_state);
        }
    }

    /// <summary>Změní stav pod zámkem a uloží ho.</summary>
    public T Update<T>(Func<CarrierState, T> update)
    {
        lock (_lock)
        {
            var result = update(_state);
            Save();
            return result;
        }
    }

    private void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state, FileJson));
        File.Move(temp, _path, overwrite: true);
    }
}

/// <summary>Náhoda pro „zlobení“ – sdílená a zamčená (Random není bezpečný pro více vláken).</summary>
public sealed class ChaosDice(IOptions<CarrierOptions> options)
{
    private readonly Lock _lock = new();
    private readonly Random _random = options.Value.Chaos.Seed is { } seed ? new Random(seed) : new Random();

    /// <summary>true s pravděpodobností <paramref name="rate"/>; 0 = nikdy, 1 = vždy.</summary>
    public bool Roll(double rate)
    {
        if (rate <= 0)
        {
            return false;
        }

        if (rate >= 1)
        {
            return true;
        }

        lock (_lock)
        {
            return _random.NextDouble() < rate;
        }
    }

    public string Digits(int count)
    {
        lock (_lock)
        {
            return string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + _random.Next(10))));
        }
    }
}
