using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Účty krok 1: registrace a ověření hesla přes UserManager se stejnými pravidly jako v aplikaci
/// (AddAccountsInfrastructure) nad SQLite v paměti. Každá akce běží ve vlastním scope jako HTTP požadavek.
/// </summary>
public sealed class AccountsTests : IDisposable
{
    private const string GoodPassword = "dlouhe heslo bez cisel";

    private readonly InMemoryDatabase _database = new();
    private readonly ServiceProvider _services;

    public AccountsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsInfrastructure(_database.ConnectionString);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Registrace_UloziJenHashHesla()
    {
        var result = await Users(m => m.CreateAsync(NewUser("jan@example.com"), GoodPassword));
        Assert.True(result.Succeeded);

        var user = await Users(m => m.FindByEmailAsync("jan@example.com"));
        Assert.NotNull(user);
        Assert.Equal("Jan", user.DisplayName);
        Assert.NotEqual(GoodPassword, user.PasswordHash);
        Assert.DoesNotContain(GoodPassword, user.PasswordHash!);
    }

    [Fact]
    public async Task Heslo_RozhodujeDelkaNeSpecialniZnaky()
    {
        var tooShort = await Users(m => m.CreateAsync(NewUser("kratke@example.com"), "Ab1!xyz"));
        var longPlain = await Users(m => m.CreateAsync(NewUser("dlouhe@example.com"), GoodPassword));

        Assert.False(tooShort.Succeeded);
        Assert.Equal(["Heslo musí mít alespoň 10 znaků."], tooShort.Errors.Select(e => e.Description));
        Assert.True(longPlain.Succeeded);
    }

    [Fact]
    public async Task DuplicitniEmail_JeOdmitnut_IKdyzSeLisiVelikostiPismen()
    {
        await Users(m => m.CreateAsync(NewUser("jan@example.com"), GoodPassword));

        var duplicate = await Users(m => m.CreateAsync(NewUser("JAN@example.com"), GoodPassword));

        Assert.False(duplicate.Succeeded);
        // Identity hlásí duplicitní jméno i e-mail – stránka registrace stejné hlášky sloučí (Distinct).
        Assert.Equal(["Účet s tímto e-mailem už existuje."], duplicate.Errors.Select(e => e.Description).Distinct());
    }

    [Fact]
    public async Task OvereniHesla_SpatneHesloNeprojde()
    {
        await Users(m => m.CreateAsync(NewUser("jan@example.com"), GoodPassword));

        var good = await Users(async m => await m.CheckPasswordAsync((await m.FindByEmailAsync("jan@example.com"))!, GoodPassword));
        var bad = await Users(async m => await m.CheckPasswordAsync((await m.FindByEmailAsync("jan@example.com"))!, "spatne heslo 123"));

        Assert.True(good);
        Assert.False(bad);
    }

    [Fact]
    public async Task Identita_ObsahujeIdAZobrazovaneJmeno()
    {
        await Users(m => m.CreateAsync(NewUser("jan@example.com"), GoodPassword));

        await using var scope = _services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var user = (await userManager.FindByEmailAsync("jan@example.com"))!;

        var principal = await factory.CreateAsync(user);

        Assert.Equal(user.Id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Jan", principal.FindFirstValue(AccountClaimTypes.DisplayName));
    }

    private static ApplicationUser NewUser(string email) =>
        new() { UserName = email, Email = email, DisplayName = "Jan" };

    private async Task<T> Users<T>(Func<UserManager<ApplicationUser>, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }
}
