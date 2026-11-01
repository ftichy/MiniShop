using System.Collections.Concurrent;
using MiniShop.Application.Inventory;

namespace MiniShop.Tests.TestDoubles;

/// <summary>E-shop krok 2B: obrázky produktů v paměti – testy nepíšou na disk.</summary>
public sealed class InMemoryProductImageStore : IProductImageStore
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public Task SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        Files[fileName] = content;
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.TryGetValue(fileName, out var content) ? content : null);

    public void Delete(string fileName) => Files.TryRemove(fileName, out _);
}
