using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.LiveUpdates;

/// <summary>
/// Vzhled krok F3a: SignalR hub živých aktualizací (<c>/hubs/live</c>). Prohlížeč se přihlásí k odběru <b>tématu</b>
/// (<see cref="LiveTopics"/>) a server mu pak při změně pošle jen signál <c>changed(téma)</c> – žádná data.
/// Co se změnilo, si stránka stáhne sama (živý výřez z kroku F2, htmx).
/// </summary>
/// <remarks>
/// Odběr = stejná oprávnění jako stránka: objednávku jen vlastník nebo zaměstnanec, který ji smí vidět; „moje objednávky“
/// podle ID účtu z přihlášení (ne od prohlížeče); Expedice a Vratky jen s politikou oblasti. Bez práva = chyba odběru.
/// </remarks>
[Authorize]
public sealed class LiveUpdatesHub(IOrderService orders, IAuthorizationService authorization) : Hub
{
    public const string Path = "/hubs/live";

    /// <summary>Jméno zprávy pro prohlížeč (live-region.js): <c>changed(téma)</c>.</summary>
    public const string ChangedMethod = "changed";

    public async Task Subscribe(string topic)
    {
        var group = await GroupForAsync(topic ?? string.Empty)
            ?? throw new HubException("Na toto téma nemáte přístup.");
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public override Task OnConnectedAsync()
    {
        LiveTelemetry.Connections.Add(1);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        LiveTelemetry.Connections.Add(-1);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Skupina SignalR pro téma – nebo null, když na něj uživatel nemá právo (nebo téma nezná).</summary>
    private async Task<string?> GroupForAsync(string topic)
    {
        var user = Context.User!;
        if (LiveTopics.TryParseOrder(topic, out var orderId))
        {
            try
            {
                await orders.GetDetailAsync(orderId, user.ToOrderAccess(), Context.ConnectionAborted);   // cizí = NotFound
                return LiveTopics.OrderGroup(orderId);
            }
            catch (NotFoundException)
            {
                return null;
            }
        }

        if (topic == LiveTopics.MyOrders)
        {
            return LiveTopics.CustomerGroup(user.ToCustomerId());
        }

        return LiveTopics.StaffPolicies.TryGetValue(topic, out var policy)
               && (await authorization.AuthorizeAsync(user, policy)).Succeeded
            ? topic
            : null;
    }
}

/// <summary>
/// Vzhled krok F3a: témata živých aktualizací. Téma = co prohlížeč odebírá (atribut <c>live-topic</c> výřezu), skupina =
/// komu server posílá. Liší se jen „moje objednávky“ – skupina podle ID účtu, prohlížeč posílá jen „my-orders“.
/// </summary>
public static class LiveTopics
{
    public const string MyOrders = "my-orders";
    public const string Fulfilment = "staff:fulfilment";
    public const string Returns = "staff:returns";
    public const string AllOrders = "staff:orders";

    public static readonly IReadOnlyDictionary<string, string> StaffPolicies = new Dictionary<string, string>
    {
        [Fulfilment] = AuthorizationPolicies.RequireFulfilment,
        [Returns] = AuthorizationPolicies.RequireReturns,
        [AllOrders] = AuthorizationPolicies.RequireOrderOverview
    };

    public static string Order(int orderId) => $"order:{orderId}";

    public static string OrderGroup(int orderId) => Order(orderId);

    public static string CustomerGroup(string customerId) => $"customer:{customerId}";

    public static bool TryParseOrder(string topic, out int orderId)
    {
        orderId = 0;
        return topic.StartsWith("order:", StringComparison.Ordinal)
               && int.TryParse(topic.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out orderId)
               && orderId > 0;
    }
}

/// <summary>Vzhled krok F3a: počet otevřených spojení na kopii Webu (metrika <c>minishop.live.connections</c>, bez ID).</summary>
public static class LiveTelemetry
{
    public const string MeterName = "MiniShop.Live";

    private static readonly Meter Meter = new(MeterName);

    public static readonly UpDownCounter<long> Connections = Meter.CreateUpDownCounter<long>(
        "minishop.live.connections", "{connection}", "Otevřená spojení živých aktualizací (SignalR) na kopii Webu");
}
