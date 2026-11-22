using System.Text.Json.Nodes;

namespace Gabinety.Booking.Agents.Abstractions;

/// <summary>An answer of the agent platform to a non-streaming call: its status code, its JSON body as it came, and <c>usage.total_tokens</c> (0 when it has none, for example on an error).</summary>
public sealed record UpstreamResult(int StatusCode, string Json, long TotalTokens);

/// <summary>
/// The agent platform is another application (docs/agentic/PLAN.md 2.4); this app only calls it through this interface. One implementation per kind:
/// <c>Fake</c> (no network, deterministic) and <c>Http</c> (an OpenAI-compatible HTTP API). The body is the OpenAI chat request; <c>model</c> is replaced by the agent slug.
/// Nothing here logs or throws a user text (message content, brief, answer).
/// </summary>
public interface IAgentUpstream
{
    string Name { get; }

    Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct);

    /// <summary>
    /// Writes the platform's raw SSE lines to <paramref name="output"/> and returns the tokens of the last <c>usage</c> chunk (0 when none came).
    /// Throws <see cref="UpstreamException"/> when the platform answers with an error status or cannot be reached BEFORE the first byte is written.
    /// </summary>
    Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct);
}

/// <summary>The platform refused or could not be reached before any byte was streamed. Carries a status code (0 for no answer) and no text of the platform or of the user.</summary>
public sealed class UpstreamException(int statusCode, Exception? inner = null)
    : Exception($"The agent platform did not answer with a success status (status {statusCode}).", inner)
{
    public int StatusCode { get; } = statusCode;
}
