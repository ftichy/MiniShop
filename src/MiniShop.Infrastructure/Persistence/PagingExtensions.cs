using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>Stránkování v databázi: nejdřív počet (COUNT), pak jen jedna stránka (OFFSET/LIMIT). Dotaz musí být seřazený.</summary>
public static class PagingExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(this IQueryable<T> ordered, PageRequest request,
        CancellationToken cancellationToken = default)
    {
        var total = await ordered.CountAsync(cancellationToken);
        var page = request.ClampTo(total);
        var items = await ordered.Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken);
        return new PagedList<T>(items, page.Page, page.Size, total);
    }
}
