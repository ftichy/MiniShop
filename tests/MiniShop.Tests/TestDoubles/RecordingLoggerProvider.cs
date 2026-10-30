using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MiniShop.Tests.TestDoubles;

/// <summary>
/// Účty krok 8: zachytí každý zápis do logu (text zprávy i hodnoty parametrů) – testy ověřují, že v logu nejsou
/// e-maily ani hesla. Serilog ve Webu zapisuje i do registrovaných providerů (writeToProviders: true).
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(" ", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            entries.Enqueue($"{category}: {formatter(state, exception)} {values} {exception}");
        }
    }
}
