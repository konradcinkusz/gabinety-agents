using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Booking.Features.AgentApi;

/// <summary>
/// The error body of the public API, in the shape OpenAI clients parse (docs/agentic/PLAN.md 2.3): <c>{"error":{"message","type","code"}}</c>, not <c>problem+json</c>.
/// The messages are fixed texts: nothing a user wrote and nothing the agent platform answered is ever put in one (rule 6).
/// </summary>
public static class OpenAiError
{
    public const string InvalidApiKey = "invalid_api_key", ModelNotFound = "model_not_found", InsufficientQuota = "insufficient_quota",
        RateLimitExceeded = "rate_limit_exceeded", UpstreamError = "upstream_error", InvalidRequest = "invalid_request_error";

    public static string Body(string message, string type, string code) =>
        JsonSerializer.Serialize(new { error = new { message, type, code } });

    public static IResult Result(int status, string message, string type, string code) =>
        Results.Content(Body(message, type, code), "application/json", System.Text.Encoding.UTF8, status);

    public static Task WriteAsync(HttpResponse response, int status, string message, string type, string code, CancellationToken ct = default)
    {
        response.StatusCode = status;
        response.ContentType = "application/json";
        return response.WriteAsync(Body(message, type, code), ct);
    }

    public static IResult Unauthorized() => Result(401, "Incorrect API key provided.", InvalidRequest, InvalidApiKey);
    public static IResult BadRequest(string message) => Result(400, message, InvalidRequest, InvalidRequest);
    public static IResult TooLarge() => Result(413, "The request body is too large.", InvalidRequest, "request_too_large");
    public static IResult ModelNotFound_() => Result(404, "The model does not exist or you have no active package for it.", InvalidRequest, ModelNotFound);
    public static IResult QuotaExhausted() => Result(429, "Your package for this model is used up.", InsufficientQuota, InsufficientQuota);
    public static IResult Upstream() => Result(502, "The agent platform did not answer.", "api_error", UpstreamError);
    public static IResult RateLimited() => Result(429, "Too many requests.", "rate_limit_error", RateLimitExceeded);
}
