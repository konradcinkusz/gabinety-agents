using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Booking;
using Gabinety.ServiceDefaults;

namespace Gabinety.Reputation.Clients;

/// <summary>Booking did not answer in budget (connection failure, timeout or 5xx after the retry). The message never carries the URL or the key.</summary>
public sealed class AccessUnavailableException(Exception? inner = null) : Exception("Booking is unavailable.", inner);

public interface IAccessClient
{
    /// <summary>GET /internal/v1/bookings/{id}/reviewability (CONTRACTS 4.3). Null when Booking answers 404.</summary>
    Task<ReviewabilityDto?> GetReviewabilityAsync(Guid accessId, CancellationToken ct);
}

/// <summary>
/// Typed client for Booking (ADR-0007, SPEC 5.1: 3 s, once). Same shape as Booking's DirectoryClient: the retry is explicit and the client is
/// built outside the shared resilience pipeline, whose defaults do not fit this budget.
/// </summary>
public sealed class AccessClient(HttpClient http, string? internalKey, TimeSpan? retryDelay = null, TimeSpan? timeout = null) : IAccessClient
{
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    readonly TimeSpan _timeout = timeout ?? CallTimeout, _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(150);

    public async Task<ReviewabilityDto?> GetReviewabilityAsync(Guid accessId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = $"internal/v1/bookings/{accessId}/reviewability";
        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await SendOnce(path, ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if ((int)response.StatusCode >= 500) { if (attempt == 1) { await Pause(ct); continue; } throw new AccessUnavailableException(); }
                if (!response.IsSuccessStatusCode) throw new AccessUnavailableException(); // 401 and the like: a configuration fault, retrying will not help
                return await response.Content.ReadFromJsonAsync<ReviewabilityDto>(ct) ?? throw new AccessUnavailableException();
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) && !ct.IsCancellationRequested)
            {
                last = ex;
                if (attempt == 1) await Pause(ct);
            }
        }
        throw new AccessUnavailableException(last);
    }

    Task Pause(CancellationToken ct) => _retryDelay > TimeSpan.Zero ? Task.Delay(_retryDelay, ct) : Task.CompletedTask;

    async Task<HttpResponseMessage> SendOnce(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(internalKey)) request.Headers.Add("X-Internal-Key", internalKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
    }
}

/// <summary>Registers the client from Backend:Booking and Internal:Keys:Booking (CONTRACTS 10.3). The key is read here and never logged.</summary>
public sealed class AccessClientFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton<IAccessClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Backend:Booking"]?.TrimEnd('/');
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2), ConnectTimeout = AccessClient.CallTimeout })
            { BaseAddress = string.IsNullOrEmpty(url) ? null : new Uri(url + "/"), Timeout = Timeout.InfiniteTimeSpan };
            return new AccessClient(http, config["Internal:Keys:Booking"]);
        });

    public void Map(IEndpointRouteBuilder endpoints) { }
}
