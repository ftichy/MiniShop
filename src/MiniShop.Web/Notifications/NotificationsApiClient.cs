using System.Net;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;
using MiniShop.ServiceDefaults;

namespace MiniShop.Web.Notifications;

/// <summary>
/// Mikroservisy M2C: co stránky Webu potřebují od služby Notifications – zařízení push a zkušební oznámení, oznámení
/// k práci, „Dejte mi vědět“. Že jde o přihlášeného (a jakou má roli), ověřuje Web; službě posílá jen ID účtu.
/// </summary>
public interface INotificationsApi
{
    Task<IReadOnlyList<PushDeviceDto>> ListDevicesAsync(string userId, CancellationToken cancellationToken = default);

    /// <returns>False = neplatné zařízení (adresa push služby neprošla pravidly).</returns>
    Task<bool> SubscribeDeviceAsync(string userId, PushSubscribeRequest request, string deviceLabel, CancellationToken cancellationToken = default);

    Task UnsubscribeDeviceAsync(string userId, string endpoint, CancellationToken cancellationToken = default);

    Task RemoveDeviceAsync(string userId, int id, CancellationToken cancellationToken = default);

    /// <returns>Na kolik zařízení zkušební oznámení odešlo.</returns>
    Task<int> SendTestAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<StaffAlertKind>> GetStaffAlertsAsync(string userId, CancellationToken cancellationToken = default);

    Task SetStaffAlertsAsync(string userId, IReadOnlyCollection<StaffAlertKind> wanted, IReadOnlyCollection<StaffAlertKind> allowed,
        CancellationToken cancellationToken = default);

    Task<bool> IsWatchingAsync(string userId, int productId, CancellationToken cancellationToken = default);

    Task WatchAsync(string userId, int productId, string productName, CancellationToken cancellationToken = default);

    Task CancelWatchAsync(string userId, int productId, CancellationToken cancellationToken = default);
}

/// <summary>Služba Notifications je nedostupná – stránka to řekne lidsky, zbytek e-shopu běží dál.</summary>
public sealed class NotificationsUnavailableException(Exception inner)
    : Exception("Služba Notifications (notifications-api) je nedostupná.", inner);

/// <summary>
/// Mikroservisy M2C: HTTP klient služby Notifications (<c>https+http://notifications-api</c>, servisní klíč
/// <c>NotificationsApi:ApiKey</c>, odolnost ze ServiceDefaults). V adrese jen ID účtu; endpoint zařízení jen v těle.
/// </summary>
public sealed class NotificationsApiClient(HttpClient http) : INotificationsApi
{
    public Task<IReadOnlyList<PushDeviceDto>> ListDevicesAsync(string userId, CancellationToken cancellationToken = default) =>
        CallAsync(async () => (IReadOnlyList<PushDeviceDto>)(await http.GetFromJsonAsync<List<PushDeviceDto>>(
            $"api/push/{Id(userId)}/devices", cancellationToken) ?? []), cancellationToken);

    public Task<bool> SubscribeDeviceAsync(string userId, PushSubscribeRequest request, string deviceLabel,
        CancellationToken cancellationToken = default) =>
        CallAsync(async () =>
        {
            using var response = await http.PostAsJsonAsync($"api/push/{Id(userId)}/devices",
                new { request.Endpoint, request.P256dh, request.Auth, DeviceLabel = deviceLabel }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            return true;
        }, cancellationToken);

    public Task UnsubscribeDeviceAsync(string userId, string endpoint, CancellationToken cancellationToken = default) =>
        SendAsync(() => http.PostAsJsonAsync($"api/push/{Id(userId)}/devices/unsubscribe", new { Endpoint = endpoint }, cancellationToken),
            cancellationToken);

    public Task RemoveDeviceAsync(string userId, int id, CancellationToken cancellationToken = default) =>
        SendAsync(() => http.DeleteAsync($"api/push/{Id(userId)}/devices/{id}", cancellationToken), cancellationToken);

    public Task<int> SendTestAsync(string userId, CancellationToken cancellationToken = default) =>
        CallAsync(async () =>
        {
            using var response = await http.PostAsync($"api/push/{Id(userId)}/test", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<SentResponse>(cancellationToken))?.Sent ?? 0;
        }, cancellationToken);

    public Task<IReadOnlySet<StaffAlertKind>> GetStaffAlertsAsync(string userId, CancellationToken cancellationToken = default) =>
        CallAsync(async () => (IReadOnlySet<StaffAlertKind>)(await http.GetFromJsonAsync<List<StaffAlertKind>>(
            $"api/staff-alerts/{Id(userId)}", cancellationToken) ?? []).ToHashSet(), cancellationToken);

    public Task SetStaffAlertsAsync(string userId, IReadOnlyCollection<StaffAlertKind> wanted, IReadOnlyCollection<StaffAlertKind> allowed,
        CancellationToken cancellationToken = default) =>
        SendAsync(() => http.PutAsJsonAsync($"api/staff-alerts/{Id(userId)}", new { Wanted = wanted, Allowed = allowed }, cancellationToken),
            cancellationToken);

    public Task<bool> IsWatchingAsync(string userId, int productId, CancellationToken cancellationToken = default) =>
        CallAsync(async () => (await http.GetFromJsonAsync<WatchResponse>(
            $"api/back-in-stock/{Id(userId)}/{productId}", cancellationToken))?.Subscribed ?? false, cancellationToken);

    public Task WatchAsync(string userId, int productId, string productName, CancellationToken cancellationToken = default) =>
        SendAsync(() => http.PutAsJsonAsync($"api/back-in-stock/{Id(userId)}/{productId}", new { ProductName = productName }, cancellationToken),
            cancellationToken);

    public Task CancelWatchAsync(string userId, int productId, CancellationToken cancellationToken = default) =>
        SendAsync(() => http.DeleteAsync($"api/back-in-stock/{Id(userId)}/{productId}", cancellationToken), cancellationToken);

    private static string Id(string userId) => Uri.EscapeDataString(userId);

    private static Task SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken) =>
        CallAsync(async () =>
        {
            using var response = await send();
            response.EnsureSuccessStatusCode();
            return true;
        }, cancellationToken);

    /// <summary>Každá porucha (síť, chybový status, timeout, circuit breaker) = nedostupná služba, ne zrušení uživatelem.</summary>
    private static async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is not NotificationsUnavailableException)
        {
            throw new NotificationsUnavailableException(ex);
        }
    }

    private sealed record SentResponse(int Sent);

    private sealed record WatchResponse(bool Subscribed);
}

/// <summary>
/// Mikroservisy M2C: Web zná jen VEŘEJNÝ klíč VAPID (prohlížeč ho potřebuje pro <c>push.js</c>); soukromý má jen služba.
/// </summary>
public sealed record PushPublicKey(string? Value)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Value);
}

public static class NotificationsApiRegistration
{
    public static IServiceCollection AddNotificationsApiClient(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = ServiceApiKey.Read(key => configuration[key], ServiceApiKey.NotificationsConfigurationKey);
        services.AddHttpClient<INotificationsApi, NotificationsApiClient>(http =>
                http.BaseAddress = new Uri("https+http://notifications-api"))
            .AddHttpMessageHandler(() => new ServiceApiKeyHandler(apiKey));
        services.AddSingleton(new PushPublicKey(configuration["PushNotifications:VapidPublicKey"]));
        return services;
    }
}
