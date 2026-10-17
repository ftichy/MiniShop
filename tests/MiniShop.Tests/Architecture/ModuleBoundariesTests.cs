using System.Reflection;
using MiniShop.Application.Abstractions;
using MiniShop.Contracts;
using MiniShop.Domain.Common;
using MiniShop.Infrastructure;
using NetArchTest.Rules;

namespace MiniShop.Tests.Architecture;

/// <summary>
/// Krok 10: test architektury hlídá hranice modulů – Ordering a Inventory se nesmí navzájem referencovat
/// v žádné vrstvě. Jediné, co sdílí, jsou kontrakty (MiniShop.Contracts) a společné jádro (outbox, inbox, bus).
/// Kdyby někdo z pohodlnosti sáhl z objednávek do skladu, test spadne dřív, než to projde code review.
/// </summary>
public sealed class ModuleBoundariesTests
{
    private static readonly Assembly[] SourceAssemblies =
    [
        typeof(Entity).Assembly,                 // MiniShop.Domain
        typeof(IUnitOfWork).Assembly,            // MiniShop.Application
        typeof(DependencyInjection).Assembly     // MiniShop.Infrastructure
    ];

    private const string Ordering = "Ordering";
    private const string Inventory = "Inventory";

    /// <summary>Namespace modulu v každé vrstvě (Domain používá "Orders").</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ModuleNamespaces = new Dictionary<string, string[]>
    {
        [Ordering] = ["MiniShop.Domain.Orders", "MiniShop.Application.Ordering", "MiniShop.Infrastructure.Ordering"],
        [Inventory] = ["MiniShop.Domain.Inventory", "MiniShop.Application.Inventory", "MiniShop.Infrastructure.Inventory"]
    };

    [Theory]
    [InlineData(Ordering, Inventory)]
    [InlineData(Inventory, Ordering)]
    public void Modul_NezavisiNaJinemModulu(string module, string forbiddenModule)
    {
        foreach (var moduleNamespace in ModuleNamespaces[module])
        {
            var result = Types.InAssemblies(SourceAssemblies)
                .That().ResideInNamespace(moduleNamespace)
                .ShouldNot().HaveDependencyOnAny(ModuleNamespaces[forbiddenModule])
                .GetResult();

            Assert.True(result.IsSuccessful,
                $"{moduleNamespace} sahá do modulu {forbiddenModule}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>Společné jádro (outbox, inbox, bus, základ DbContextu) nesmí znát žádný modul – jinak by nešlo sdílet.</summary>
    [Theory]
    [InlineData("MiniShop.Domain.Common")]
    [InlineData("MiniShop.Application.Abstractions")]
    [InlineData("MiniShop.Infrastructure.Outbox")]
    [InlineData("MiniShop.Infrastructure.Inbox")]
    [InlineData("MiniShop.Infrastructure.Integration")]
    [InlineData("MiniShop.Infrastructure.Persistence")]
    [InlineData("MiniShop.Infrastructure.Events")]
    public void SpolecneJadro_NezavisiNaModulech(string sharedNamespace)
    {
        var result = Types.InAssemblies(SourceAssemblies)
            .That().ResideInNamespace(sharedNamespace)
            .ShouldNot().HaveDependencyOnAny([.. ModuleNamespaces[Ordering], .. ModuleNamespaces[Inventory]])
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{sharedNamespace} závisí na modulu: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>Kontrakty sdílí všechny služby – nesmí táhnout s sebou nic dalšího z MiniShopu.</summary>
    [Fact]
    public void Kontrakty_NezavisiNaNicemDalsim()
    {
        var result = Types.InAssembly(typeof(IIntegrationMessage).Assembly)
            .ShouldNot().HaveDependencyOnAny("MiniShop.Domain", "MiniShop.Application", "MiniShop.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
