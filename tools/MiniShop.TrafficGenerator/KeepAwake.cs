using System.Runtime.InteropServices;

namespace MiniShop.TrafficGenerator;

/// <summary>
/// Dlouhý test: ať se PC během běhu neuspí. Uspání (i „moderní úsporný režim“) odpojí síť – generátor pak hlásí
/// chyby, které nezpůsobil MiniShop (22. 11. 2026: 6 falešných chyb po 7 minutách bez sítě).
/// Windows: <c>SetThreadExecutionState</c> – platí, dokud proces běží (nebo do <see cref="Dispose"/>), nic se nenastavuje
/// natrvalo. I obrazovka zůstane svítit: na PC s moderním úsporným režimem (tento případ) zhasnutí obrazovky
/// samo spustí úsporný režim. Zavřené víko nebo tlačítko napájení PC uspí vždy. Na jiných systémech nedělá nic.
/// </summary>
internal sealed class KeepAwake : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    private readonly bool _active;

    public KeepAwake()
    {
        _active = OperatingSystem.IsWindows() && SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) != 0;
    }

    public bool IsActive => _active;

    public void Dispose()
    {
        if (_active)
        {
            SetThreadExecutionState(EsContinuous);
        }
    }

    // Stav patří vláknu, které ho nastavilo (hlavní vlákno čeká na konec programu), a skončí s procesem.
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
