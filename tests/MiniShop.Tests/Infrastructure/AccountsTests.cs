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

    // --- Účty krok 2: role a první admin (AccountsSeeder) ---

    private static readonly AdminBootstrapSettings Bootstrap = new("admin@example.com", "admin-heslo-123");

    [Fact]
    public async Task Seed_VytvoriPrvnihoAdminaSPovinnouZmenouHesla_JenJednou()
    {
        await Seed(Bootstrap);
        await Seed(Bootstrap with { Email = "druhy-admin@example.com" });

        var admins = await Users(m => m.GetUsersInRoleAsync(AccountRoles.Admin));
        var admin = Assert.Single(admins);
        Assert.Equal("admin@example.com", admin.Email);
        Assert.True(await Users(m => m.CheckPasswordAsync(admin, "admin-heslo-123")));
        var claims = await Users(m => m.GetClaimsAsync(admin));
        Assert.Contains(claims, c => c.Type == AccountClaimTypes.MustChangePassword);
    }

    [Fact]
    public async Task Seed_NepovysiExistujiciUcetNaAdmina()
    {
        // Někdo si adminův e-mail zaregistroval dřív, než seed proběhl.
        await Users(m => m.CreateAsync(NewUser("admin@example.com"), GoodPassword));

        await Seed(Bootstrap);

        Assert.Empty(await Users(m => m.GetUsersInRoleAsync(AccountRoles.Admin)));
    }

    [Fact]
    public async Task Seed_BezKonfigurace_AdminNevznikne()
    {
        await Seed(null);

        Assert.Empty(await Users(m => m.GetUsersInRoleAsync(AccountRoles.Admin)));
    }

    [Fact]
    public async Task Seed_UctumBezRoleDoplniRoliUser()
    {
        await Users(m => m.CreateAsync(NewUser("stary@example.com"), GoodPassword));   // účet z kroku 1

        await Seed(Bootstrap);

        var user = (await Users(m => m.FindByEmailAsync("stary@example.com")))!;
        Assert.Equal([AccountRoles.User], await Users(m => m.GetRolesAsync(user)));
        var admin = (await Users(m => m.FindByEmailAsync("admin@example.com")))!;
        Assert.Equal([AccountRoles.Admin], await Users(m => m.GetRolesAsync(admin)));   // admin není zákazník
    }

    [Fact]
    public async Task CreateFirstAdmin_KdyzAdminExistuje_Odmitne()
    {
        await Seed(Bootstrap);

        var result = await Seeder(s => s.CreateFirstAdminAsync("dalsi@example.com", "Další", GoodPassword, mustChangePassword: false));

        Assert.False(result.Succeeded);
        Assert.Null(await Users(m => m.FindByEmailAsync("dalsi@example.com")));
    }

    [Fact]
    public async Task CreateFirstAdmin_Prikazem_BezPovinneZmenyHesla()
    {
        var result = await Seeder(s => s.CreateFirstAdminAsync("admin@example.com", "Jan", GoodPassword, mustChangePassword: false));

        Assert.True(result.Succeeded);
        var admin = (await Users(m => m.FindByEmailAsync("admin@example.com")))!;
        Assert.True(await Users(m => m.IsInRoleAsync(admin, AccountRoles.Admin)));
        Assert.Empty(await Users(m => m.GetClaimsAsync(admin)));
    }

    [Fact]
    public async Task Identita_ObsahujeRoli()
    {
        await Seed(Bootstrap);

        await using var scope = _services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();

        var principal = await factory.CreateAsync((await userManager.FindByEmailAsync("admin@example.com"))!);

        Assert.True(principal.IsInRole(AccountRoles.Admin));
        Assert.True(principal.HasClaim(AccountClaimTypes.MustChangePassword, "true"));
    }

    private Task Seed(AdminBootstrapSettings? bootstrap) => Seeder(async s =>
    {
        await s.SeedAsync(bootstrap);
        return true;
    });

    private async Task<T> Seeder<T>(Func<AccountsSeeder, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountsSeeder>());
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
