namespace MiniShop.Tests.TestDoubles;

/// <summary>Čas pod kontrolou testu: stojí, dokud ho test neposune (<see cref="Advance"/>, např. kvůli timeoutu ságy).</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
