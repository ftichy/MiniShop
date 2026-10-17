using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Events;

namespace MiniShop.Tests.Infrastructure;

/// <summary>
/// Krok 2 EDA: dispatcher doručí událost všem a jen těm handlerům, které ji odebírají.
/// </summary>
public class DomainEventDispatcherTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Recorder
    {
        public List<string> Calls { get; } = new();
    }

    private sealed class FirstPlacedHandler(Recorder recorder) : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced e, CancellationToken ct) { recorder.Calls.Add("first:placed"); return Task.CompletedTask; }
    }

    private sealed class SecondPlacedHandler(Recorder recorder) : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced e, CancellationToken ct) { recorder.Calls.Add("second:placed"); return Task.CompletedTask; }
    }

    private sealed class CancelledHandler(Recorder recorder) : IDomainEventHandler<OrderCancelled>
    {
        public Task HandleAsync(OrderCancelled e, CancellationToken ct) { recorder.Calls.Add("cancelled"); return Task.CompletedTask; }
    }

    private sealed class FailingPlacedHandler : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced e, CancellationToken ct) => throw new InvalidOperationException("Test");
    }

    private static (DomainEventDispatcher Dispatcher, Recorder Recorder) Create(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        register(services);
        var provider = services.BuildServiceProvider();
        return (new DomainEventDispatcher(provider, NullLogger<DomainEventDispatcher>.Instance), provider.GetRequiredService<Recorder>());
    }

    private static Order NewOrder() => new("Jan Novák", "jan@example.com", Now);

    [Fact]
    public async Task Dispatch_ZavolaVsechnyHandleryDaneUdalosti()
    {
        var (dispatcher, recorder) = Create(s =>
        {
            s.AddScoped<IDomainEventHandler<OrderPlaced>, FirstPlacedHandler>();
            s.AddScoped<IDomainEventHandler<OrderPlaced>, SecondPlacedHandler>();
            s.AddScoped<IDomainEventHandler<OrderCancelled>, CancelledHandler>();
        });

        await dispatcher.DispatchAsync([new OrderPlaced(NewOrder(), Now)]);

        Assert.Equal(new[] { "first:placed", "second:placed" }, recorder.Calls);
    }

    [Fact]
    public async Task Dispatch_UdalostBezHandleru_NicSeNestane()
    {
        var (dispatcher, recorder) = Create(_ => { });

        await dispatcher.DispatchAsync([new OrderCompleted(NewOrder(), Now)]);

        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Dispatch_ChybaVJednomHandleru_OstatniBeziDal()
    {
        var (dispatcher, recorder) = Create(s =>
        {
            s.AddScoped<IDomainEventHandler<OrderPlaced>, FailingPlacedHandler>();
            s.AddScoped<IDomainEventHandler<OrderPlaced>, SecondPlacedHandler>();
        });

        await dispatcher.DispatchAsync(new IDomainEvent[] { new OrderPlaced(NewOrder(), Now) });

        Assert.Equal(new[] { "second:placed" }, recorder.Calls);
    }
}
