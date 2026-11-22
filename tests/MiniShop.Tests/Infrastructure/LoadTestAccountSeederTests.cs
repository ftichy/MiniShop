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
        Assert.Equal(3, second.Reset);
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

    /// <summary>
    /// Dlouhý test: účty 1–4 z dřívějška, další nově – druhé spuštění s jiným heslem musí sjednotit heslo všech
    /// a zrušit blokaci (po testu se účty blokují). Klíč 2FA skladníka zůstane.
    /// </summary>
    [Fact]
    public async Task Seed_ExistujicimUctum_NastaviZadaneHeslo_ZrusiBlokaci_KlicSkladnikaZustane()
    {
        const string newPassword = "jine dlouhe heslo bez cisel";
        var first = await SeedAsync(1);
        await using (var scope = _services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var blocked = (await users.FindByEmailAsync(LoadTestAccountSeeder.CustomerEmail(1)))!;
            await users.SetLockoutEndDateAsync(blocked, DateTimeOffset.MaxValue);
        }

        var second = await SeedAsync(3, newPassword);

        Assert.Empty(second.Errors);
        Assert.Equal(2, second.Created);   // zákazníci 2 a 3
        Assert.Equal(2, second.Reset);     // zákazník 1 + skladník
        Assert.Equal(first.StaffTotpKey, second.StaffTotpKey);

        await using var check = _services.CreateAsyncScope();
        var userManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var email in new[] { LoadTestAccountSeeder.CustomerEmail(1), LoadTestAccountSeeder.CustomerEmail(3), LoadTestAccountSeeder.StaffEmail })
        {
            var user = (await userManager.FindByEmailAsync(email))!;
            Assert.True(await userManager.CheckPasswordAsync(user, newPassword), email);
            Assert.False(await userManager.CheckPasswordAsync(user, Password), email);
            Assert.False(await userManager.IsLockedOutAsync(user), email);
        }
    }

    /// <summary>Heslo, které nesplní pravidla, se pozná předem – nevznikne žádný účet (dřív vznikla jen část).</summary>
    [Fact]
    public async Task Seed_KratkeHeslo_NicNezalozi()
    {
        var result = await SeedAsync(2, "kratke");

        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.Created);
        await using var scope = _services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(LoadTestAccountSeeder.CustomerEmail(1)));
        Assert.Null(await users.FindByEmailAsync(LoadTestAccountSeeder.StaffEmail));
    }

    private async Task<LoadTestAccountsResult> SeedAsync(int customers, string password = Password)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LoadTestAccountSeeder>().SeedAsync(customers, password);
    }

    public void Dispose()
    {
        _services.Dispose();
        _databases.Dispose();
    }
}
