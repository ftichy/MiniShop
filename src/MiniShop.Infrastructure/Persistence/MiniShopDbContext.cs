using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Products;

namespace MiniShop.Infrastructure.Persistence;

/// <summary>
/// EF Core kontext. Zároveň slouží jako Unit of Work – metoda SaveChangesAsync už existuje v DbContext.
/// </summary>
public sealed class MiniShopDbContext(DbContextOptions<MiniShopDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiniShopDbContext).Assembly);
    }
}
