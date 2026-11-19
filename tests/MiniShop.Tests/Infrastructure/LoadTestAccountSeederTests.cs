using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>Generátor provozu (issue #155): testovací účty z příkazu <c>create-loadtest-users</c>.</summary>
public sealed class LoadTestAccountSeederTests : IDisposable
{
    private const string Password = "dlouhe heslo bez cisel";

    private readonly TestDatabases _databases = new();
    private readonly ServiceProvider _services;

    public LoadTestAccountSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsInfrastructure(_databases.Accounts);
        _services = services.BuildServiceProvider();
        AccountsDatabaseInitializer.InitializeAsync(_services, null).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Seed_ZaloziZakazniky_SkladnikaSDvoufazovymOverenim_AJeIdempotentni()
    {
        var first = await SeedAsync(2);
        var second = await SeedAsync(2);

        Assert.Empty(first.Errors);
        Assert.Equal(3, first.Created);   // 2 zákazníci + skladník
        Assert.Equal(0, second.Created);
        Assert.Equal(first.StaffTotpKey, second.StaffTotpKey);
        Assert.NotEmpty(first.StaffTotpKey);

        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var customer = (await users.FindByEmailAsync(LoadTestAccountSeeder.CustomerEmail(1)))!;
        Assert.True(customer.EmailConfirmed);
        Assert.EndsWith(".invalid", customer.Email);
        Assert.Equal([AccountRoles.User], await users.GetRolesAsync(customer));

        var staff = (await users.FindByEmailAsync(LoadTestAccountSeeder.StaffEmail))!;
        Assert.True(staff.TwoFactorEnabled);
        Assert.Equal([AccountRoles.Warehouse], await users.GetRolesAsync(staff));   // nejmenší potřebná práva
    }

    private async Task<LoadTestAccountsResult> SeedAsync(int customers)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LoadTestAccountSeeder>().SeedAsync(customers, Password);
    }

    public void Dispose()
    {
        _services.Dispose();
        _databases.Dispose();
    }
}
