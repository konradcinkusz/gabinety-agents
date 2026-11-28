using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Access;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// <c>POST /v1/chat/completions</c> (docs/agentic/PLAN.md B-04): the OpenAI chat API for the agents the client has a <c>Chat</c> package for. The body goes to the platform as it
/// came (<c>tools</c>, <c>tool_choice</c> and the rest included) with <c>model</c> replaced by the slug of the package. Tokens are settled after the fact from the platform's own
/// <c>usage.total_tokens</c>, so the last request may take a package past its total; nothing is charged when the platform fails before the first byte.
/// What is logged: package id, agent slug (the package's, not the request's), tokens, status, milliseconds. Never a message, the model text of the request or the key (rules 6 and 7).
/// </summary>
public sealed class ChatCompletionsFeature : IServiceFeature
{
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/v1/chat/completions", CompleteAsync).RequireAuthorization(ApiKeyAuthentication.PolicyName);

    static async Task<IResult> CompleteAsync(HttpContext http, ClaimsPrincipal user, EntitlementStore entitlements, IAgentUpstream upstream,
        ILogger<ChatCompletionsFeature> log, CancellationToken ct)
    {
        var (body, error) = await ReadBodyAsync(http.Request, ct);
        if (error is not null) return error;
        if (body!["model"] is not JsonValue m || !m.TryGetValue<string>(out var model) || string.IsNullOrWhiteSpace(model))
            return OpenAiError.BadRequest("The model field is required.");

        var sub = user.FindFirst("sub")!.Value;
        var package = await entitlements.FindAsync(sub, model, PackageKind.Chat, null, ct);
        if (package is null)
            return await entitlements.HasExhaustedAsync(sub, model, PackageKind.Chat, null, ct) ? OpenAiError.QuotaExhausted() : OpenAiError.ModelNotFound_();

        Guid.TryParse(user.FindFirst(ApiKeyAuthentication.KeyIdClaim)?.Value, out var keyId);
        var apiKeyId = keyId == Guid.Empty ? (Guid?)null : keyId;
        var stream = body["stream"] is JsonValue s && s.TryGetValue<bool>(out var on) && on;
        var clock = Stopwatch.StartNew();
        return stream
            ? await StreamAsync(http, package, apiKeyId, body, entitlements, upstream, log, clock, ct)
            : await NonStreamAsync(package, apiKeyId, body, entitlements, upstream, log, clock, ct);
    }

    static async Task<IResult> NonStreamAsync(BookingEntity package, Guid? apiKeyId, JsonObject body, EntitlementStore entitlements, IAgentUpstream upstream,
        ILogger log, Stopwatch clock, CancellationToken ct)
    {
        UpstreamResult result;
        try { result = await upstream.CompleteAsync(package.PractitionerSlug, body, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogOutcome(log, package, 0, 502, clock, ex.GetType().Name);
            return OpenAiError.Upstream();
        }

        if (result.StatusCode >= 500)
        {
            LogOutcome(log, package, 0, 502, clock, null);
            return OpenAiError.Upstream();
        }
        // The platform's answer (a 4xx included) goes back as it came.
        if (result.StatusCode is >= 200 and < 300 && result.TotalTokens > 0)
            await entitlements.ConsumeAndRecordAsync(package, result.TotalTokens, apiKeyId, CancellationToken.None);
        LogOutcome(log, package, result.StatusCode is >= 200 and < 300 ? result.TotalTokens : 0, result.StatusCode, clock, null);
        return Results.Content(result.Json, "application/json", System.Text.Encoding.UTF8, result.StatusCode);
    }

    static async Task<IResult> StreamAsync(HttpContext http, BookingEntity package, Guid? apiKeyId, JsonObject body, EntitlementStore entitlements,
        IAgentUpstream upstream, ILogger log, Stopwatch clock, CancellationToken ct)
    {
        var response = http.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        long tokens = 0;
        string? failure = null;
        try { tokens = await upstream.StreamAsync(package.PractitionerSlug, body, response.Body, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { failure = "ClientDisconnected"; }
        catch (Exception ex) { failure = ex.GetType().Name; }
        finally
        {
            // Also after a disconnect: the platform already did the work. The request token is cancelled then, so settle with a fresh one.
            if (tokens > 0) await entitlements.ConsumeAndRecordAsync(package, tokens, apiKeyId, CancellationToken.None);
        }

        if (!response.HasStarted && failure is not null)
        {   // nothing was sent yet: an honest 502 instead of an empty 200
            response.Headers.Remove("Cache-Control");
            response.Headers.Remove("X-Accel-Buffering");
            LogOutcome(log, package, 0, 502, clock, failure);
            return OpenAiError.Upstream();
        }
        LogOutcome(log, package, tokens, response.StatusCode, clock, failure);
        return Results.Empty;
    }

    static async Task<(JsonObject? Body, IResult? Error)> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes) return (null, OpenAiError.TooLarge());
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) return (null, OpenAiError.TooLarge());
            buffer.Write(chunk, 0, read);
        }
        try
        {
            if (JsonNode.Parse(buffer.ToArray()) is JsonObject o) return (o, null);
        }
        catch (JsonException) { /* the parser's message may quote the body: say nothing of it */ }
        return (null, OpenAiError.BadRequest("The request body must be a JSON object."));
    }

    static void LogOutcome(ILogger log, BookingEntity package, long tokens, int status, Stopwatch clock, string? failure) =>
        log.LogInformation("Chat completion for package {BookingId} agent {AgentSlug}: {Tokens} tokens, status {Status}, {ElapsedMs} ms, failure {Failure}",
            package.Id, package.PractitionerSlug, tokens, status, clock.ElapsedMilliseconds, failure ?? "none");
}
