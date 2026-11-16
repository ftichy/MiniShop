using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Tests.TestDoubles;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Účty krok 6: správa uživatelů (<see cref="AccountAdministration"/>) nad PostgreSQL (Testcontainers) – pravidla
/// (poslední admin, sám sebe, jen aktivní admin), audit, security stamp a migrace users.db.
/// </summary>
public sealed class AccountAdministrationTests : IDisposable
{
    private const string Password = "dlouhe heslo bez cisel";

    private readonly TestDatabases _databases = new();   // škálování krok 2 a 3C: PostgreSQL (Testcontainers)
    private readonly ServiceProvider _services;
    private string _adminId;   // škálování krok 3C: test souběhu ho mění podle toho, kdo zůstal adminem

    public AccountAdministrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAccountsInfrastructure(_databases.Accounts);
        _services = services.BuildServiceProvider();

        AccountsDatabaseInitializer.InitializeAsync(_services, new AdminBootstrapSettings("admin@example.com", Password))
            .GetAwaiter().GetResult();
        _adminId = UserId("admin@example.com");
    }

    /// <summary>Účty krok 12A: víc rolí najednou (i zákazník + zaměstnanec), audit s detailem, nový security stamp.</summary>
    [Fact]
    public async Task Role_VicNajednou_ZapiseAuditSDetailem_VymeniSecurityStamp()
    {
        var janId = await CreateUserAsync("jan@example.com");
        var stampBefore = await StampAsync(janId);

        var result = await Admin(a => a.SetRolesAsync(_adminId, janId, [AccountRoles.Warehouse, AccountRoles.User, AccountRoles.Marketing]));

        Assert.True(result.Succeeded, result.Message);
        var jan = (await Admin(a => a.GetAccountAsync(janId)))!;
        Assert.Equal([AccountRoles.User, AccountRoles.Warehouse, AccountRoles.Marketing], jan.Roles);   // pořadí AccountRoles.All
        Assert.True(jan.IsStaff && jan.IsCustomer);
        Assert.NotEqual(stampBefore, await StampAsync(janId));
        Assert.Equal([AccountRoles.Warehouse, AccountRoles.Marketing], result.RoleChange!.Added);
        Assert.Empty(result.RoleChange.Removed);
        Assert.True(result.RoleChange.ChangesStaffAccess);

        var entry = Assert.Single(await Admin(a => a.GetAuditAsync(janId, 10)));
        Assert.Equal((AccountAuditAction.RolesChanged, "+Sklad a expedice, +Marketing"), (entry.Action, entry.Detail));
        Assert.Equal("admin@example.com", entry.ActorEmail);
        Assert.Equal("jan@example.com", entry.TargetEmail);

        // Odebrat zaměstnaneckou roli = zase změna oprávnění (e-mail); jen zákaznická role e-mail nespouští.
        var back = await Admin(a => a.SetRolesAsync(_adminId, janId, [AccountRoles.User, AccountRoles.Marketing]));
        Assert.Equal("−Sklad a expedice", back.RoleChange!.Describe());
        Assert.False((await Admin(a => a.SetRolesAsync(_adminId, janId, [AccountRoles.Marketing]))).RoleChange!.ChangesStaffAccess);
    }

    /// <summary>Účty krok 12A: správce si sám přidá roli Zákazník – smí; správu uživatelů si ale neodebere.</summary>
    [Fact]
    public async Task Spravce_SiPridaZakaznika_SpravuSiNeodebere()
    {
        var result = await Admin(a => a.SetRolesAsync(_adminId, _adminId, [.. AccountRoles.Staff, AccountRoles.User]));

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.RoleChange!.ChangesStaffAccess);
        Assert.True((await Admin(a => a.GetAccountAsync(_adminId)))!.IsCustomer);
        Assert.False((await Admin(a => a.SetRolesAsync(_adminId, _adminId, [AccountRoles.User]))).Succeeded);
        Assert.False((await Admin(a => a.SetRolesAsync(_adminId, _adminId, ["Hacker"]))).Succeeded);   // neznámá role
        Assert.Equal("Role se nezměnily.",
            (await Admin(a => a.SetRolesAsync(_adminId, _adminId, [.. AccountRoles.Staff, AccountRoles.User]))).Message);
    }

    /// <summary>Účty krok 12A: zaměstnanec bez role správce uživatelů role nepřiděluje (ani sobě).</summary>
    [Fact]
    public async Task JinyZamestnanec_RoleNepridelujeAniSobe()
    {
        var petraId = await CreateUserAsync("petra@example.com");
        await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.Warehouse]));

        var result = await Admin(a => a.SetRolesAsync(petraId, petraId, [AccountRoles.Warehouse, AccountRoles.UserAdmin]));

        Assert.Equal("Uživatele smí spravovat jen aktivní správce uživatelů.", result.Message);
    }

    /// <summary>
    /// Účty krok 12A: migrace StaffRoles převede původní roli Admin na všechny zaměstnanecké role (Admin zůstane – starší
    /// verze kódu); uložení rolí v aplikaci ji pak odebere.
    /// </summary>
    [Fact]
    public async Task Migrace_PuvodniAdminDostaneVsechnyZamestnaneckeRole_UlozeniHoUklidi()
    {
        var oldAdminId = await CreateUserAsync("stary-admin@example.com");
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db).MigrateAsync("20261113081925_LoginAttempts");
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await roles.CreateAsync(new IdentityRole(AccountRoles.LegacyAdmin));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var oldAdmin = (await users.FindByIdAsync(oldAdminId))!;
            await users.RemoveFromRoleAsync(oldAdmin, AccountRoles.User);
            await users.AddToRoleAsync(oldAdmin, AccountRoles.LegacyAdmin);
            await db.Database.MigrateAsync();   // znovu StaffRoles
        }

        var rolesAfter = await Users(async m => await m.GetRolesAsync((await m.FindByIdAsync(oldAdminId))!));
        Assert.Equal(AccountRoles.Staff.Append(AccountRoles.LegacyAdmin).Order(), rolesAfter.Order());
        Assert.Equal(AccountRoles.Staff, (await Admin(a => a.GetAccountAsync(oldAdminId)))!.Roles);   // původní Admin se neukazuje

        Assert.True((await Admin(a => a.SetRolesAsync(_adminId, oldAdminId, AccountRoles.Staff))).Succeeded);   // jen úklid
        Assert.DoesNotContain(AccountRoles.LegacyAdmin, await Users(async m => await m.GetRolesAsync((await m.FindByIdAsync(oldAdminId))!)));
    }

    [Fact]
    public async Task Zablokovani_UzivatelSeNeprihlasi_OdblokovaniVratiPristup()
    {
        var janId = await CreateUserAsync("jan@example.com");

        Assert.True((await Admin(a => a.LockAsync(_adminId, janId))).Succeeded);
        Assert.True((await Admin(a => a.GetAccountAsync(janId)))!.IsBlocked);
        Assert.True(await Users(async m => await m.IsLockedOutAsync((await m.FindByIdAsync(janId))!)));

        Assert.True((await Admin(a => a.UnlockAsync(_adminId, janId))).Succeeded);
        Assert.False((await Admin(a => a.GetAccountAsync(janId)))!.IsBlocked);

        Assert.Equal(
            [AccountAuditAction.Unlocked, AccountAuditAction.Locked],
            (await Admin(a => a.GetAuditAsync(janId, 10))).Select(entry => entry.Action));
    }

    [Fact]
    public async Task PosledniAdmin_NejdeVratitNaUseraAniZablokovat()
    {
        var demote = await Admin(a => a.SetRolesAsync(_adminId, _adminId, [AccountRoles.User]));
        var lockOut = await Admin(a => a.LockAsync(_adminId, _adminId));

        Assert.Contains("sám", demote.Message);   // sám sobě správu uživatelů neodebere
        Assert.Equal("Posledního správce uživatelů nejde zablokovat.", lockOut.Message);
        Assert.True((await Admin(a => a.GetAccountAsync(_adminId)))!.Roles.Contains(AccountRoles.UserAdmin));
        Assert.Empty(await Admin(a => a.GetAuditAsync(null, 10)));
    }

    [Fact]
    public async Task SamSebe_NejdeAniKdyzJsouDvaAdmini()
    {
        var petraId = await CreateUserAsync("petra@example.com");
        await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.UserAdmin]));

        var demote = await Admin(a => a.SetRolesAsync(_adminId, _adminId, [AccountRoles.User]));
        var lockOut = await Admin(a => a.LockAsync(_adminId, _adminId));

        Assert.False(demote.Succeeded);
        Assert.False(lockOut.Succeeded);
        Assert.Contains("sám", demote.Message);
        Assert.Contains("Sám sebe", lockOut.Message);
    }

    [Fact]
    public async Task DruhyAdmin_VratiPrvniho_PakUzPrvniNicNesmi()
    {
        var petraId = await CreateUserAsync("petra@example.com");
        await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.UserAdmin]));

        Assert.True((await Admin(a => a.SetRolesAsync(petraId, _adminId, [AccountRoles.User]))).Succeeded);

        // Původní admin má možná ještě starou cookie s rolí Admin – rozhoduje ale databáze.
        var result = await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.User]));
        Assert.Equal("Uživatele smí spravovat jen aktivní správce uživatelů.", result.Message);
        Assert.True((await Admin(a => a.GetAccountAsync(petraId)))!.Roles.Contains(AccountRoles.UserAdmin));
    }

    /// <summary>
    /// Škálování krok 3C: dva admini se ve stejné chvíli navzájem vracejí na usera. V PostgreSQL běží obě transakce
    /// souběžně a každá by viděla, že ten druhý je ještě admin – bez zámku by obchod zůstal bez admina.
    /// </summary>
    [Fact]
    public async Task DvaAdminiNaraz_NavzajemSeOdeberou_VzdyZustaneAdmin()
    {
        for (var round = 0; round < 10; round++)
        {
            var petraId = await CreateUserAsync($"petra{round}@example.com");
            await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.UserAdmin]));

            var results = await Task.WhenAll(
                Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.User])),
                Admin(a => a.SetRolesAsync(petraId, _adminId, [AccountRoles.User])));

            Assert.Single(results, r => r.Succeeded);
            var accounts = await Admin(a => a.GetAccountsAsync());
            var remaining = Assert.Single(accounts, a => a.Roles.Contains(AccountRoles.UserAdmin));

            // Další kolo s tím, kdo zůstal adminem.
            _adminId = remaining.Id;
        }
    }

    [Fact]
    public async Task ZablokovanyAdmin_NesmiSpravovat()
    {
        var petraId = await CreateUserAsync("petra@example.com");
        var janId = await CreateUserAsync("jan@example.com");
        await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.UserAdmin]));
        await Admin(a => a.LockAsync(_adminId, petraId));

        var result = await Admin(a => a.LockAsync(petraId, janId));

        Assert.False(result.Succeeded);
        Assert.False((await Admin(a => a.GetAccountAsync(janId)))!.IsBlocked);
    }

    [Fact]
    public async Task Prehled_ObsahujeRoleAStav()
    {
        var janId = await CreateUserAsync("jan@example.com");
        await Admin(a => a.LockAsync(_adminId, janId));

        var accounts = await Admin(a => a.GetAccountsAsync());

        Assert.Equal(["admin@example.com", "jan@example.com"], accounts.Select(a => a.Email));
        Assert.True(accounts[0].Roles.Contains(AccountRoles.UserAdmin));
        Assert.False(accounts[0].IsBlocked);
        Assert.False(accounts[1].Roles.Contains(AccountRoles.UserAdmin));
        Assert.True(accounts[1].IsBlocked);
        Assert.Null(accounts[1].LockedOutUntil);
    }

    // --- Účty krok 8: dočasný zámek po špatných heslech ---

    [Fact]
    public async Task PetSpatnychHesel_DocasnyZamek_NeniBlokace_AdminHoMuzeZrusit()
    {
        var janId = await CreateUserAsync("jan@example.com");
        await FailPasswordAsync(janId, AccountsInfrastructure.MaxFailedAccessAttempts);

        var jan = (await Admin(a => a.GetAccountAsync(janId)))!;
        Assert.False(jan.IsBlocked);
        Assert.NotNull(jan.LockedOutUntil);

        Assert.True((await Admin(a => a.UnlockAsync(_adminId, janId))).Succeeded);
        Assert.Null((await Admin(a => a.GetAccountAsync(janId)))!.LockedOutUntil);
    }

    [Fact]
    public async Task DocasneZamcenyUcet_JdeZablokovat()
    {
        var janId = await CreateUserAsync("jan@example.com");
        await FailPasswordAsync(janId, AccountsInfrastructure.MaxFailedAccessAttempts);

        Assert.True((await Admin(a => a.LockAsync(_adminId, janId))).Succeeded);
        Assert.True((await Admin(a => a.GetAccountAsync(janId)))!.IsBlocked);
    }

    [Fact]
    public async Task DocasneZamcenyAdmin_ZustavaAktivnimAdminem()
    {
        // Útočník zkoušením hesel nesmí „odstavit“ admina – zámek jen brání přihlášení, práva nebere.
        var petraId = await CreateUserAsync("petra@example.com");
        await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.UserAdmin]));
        await FailPasswordAsync(_adminId, AccountsInfrastructure.MaxFailedAccessAttempts);

        var result = await Admin(a => a.SetRolesAsync(_adminId, petraId, [AccountRoles.User]));

        Assert.True(result.Succeeded, result.Message);
    }

    private Task FailPasswordAsync(string userId, int times) => Users(async m =>
    {
        var user = (await m.FindByIdAsync(userId))!;
        for (var i = 0; i < times; i++)
        {
            await m.AccessFailedAsync(user);
        }

        return true;
    });

    private async Task<string> CreateUserAsync(string email)
    {
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        // Obojí v jednom scope (jednom DbContextu) – jinak by se instance uživatele hádala s načtenou kopií.
        await Users(async m =>
        {
            Assert.True((await m.CreateAsync(user, Password)).Succeeded);
            return await m.AddToRoleAsync(user, AccountRoles.User);
        });
        return user.Id;
    }

    private string UserId(string email) =>
        Users(m => m.FindByEmailAsync(email)).GetAwaiter().GetResult()!.Id;

    private Task<string?> StampAsync(string userId) =>
        Users(async m => (await m.FindByIdAsync(userId))!.SecurityStamp);

    private async Task<T> Admin<T>(Func<AccountAdministration, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccountAdministration>());
    }

    private async Task<T> Users<T>(Func<UserManager<ApplicationUser>, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }

    public void Dispose()
    {
        _services.Dispose();
        _databases.Dispose();
    }
}
