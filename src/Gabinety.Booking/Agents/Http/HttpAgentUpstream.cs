using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Booking.Agents.Abstractions;

namespace Gabinety.Booking.Agents.Http;

/// <summary>
/// The real platform (<c>Agents:Provider=Http</c>): <c>POST {Agents:Upstream:BaseUrl}/v1/chat/completions</c> with <c>Authorization: Bearer {Agents:Upstream:ApiKey}</c>
/// (the client is configured by <see cref="AgentsFeature"/>). <c>model</c> becomes the agent slug; a stream always asks for <c>stream_options.include_usage</c>.
/// The platform's body, the request body and the key are never logged and never put in an exception message.
/// </summary>
public sealed class HttpAgentUpstream(HttpClient http) : IAgentUpstream
{
    public const string Path = "v1/chat/completions";

    public string Name => "Http";

    public async Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct)
    {
        var request = Build(agentSlug, body, stream: false);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            return new UpstreamResult((int)response.StatusCode, json, response.IsSuccessStatusCode ? TotalTokensOf(json) : 0);
        }
        catch (HttpRequestException e) { throw new UpstreamException(0, e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw new UpstreamException(0, e); } // the HttpClient timeout
    }

    public async Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct)
    {
        var request = Build(agentSlug, body, stream: true);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException e) { throw new UpstreamException(0, e); }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested) { throw new UpstreamException(0, e); }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new UpstreamException((int)response.StatusCode);
            long tokens = 0;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), ct);
                if (line.Length == 0) await output.FlushAsync(ct); // an empty line ends one SSE event
                if (line.StartsWith("data:", StringComparison.Ordinal) && line.Contains("\"usage\"", StringComparison.Ordinal))
                    tokens = TotalTokensOf(line.AsSpan(5).Trim().ToString()) is var t and > 0 ? t : tokens;
            }
            await output.FlushAsync(ct);
            return tokens;
        }
    }

    static HttpRequestMessage Build(string agentSlug, JsonObject body, bool stream)
    {
        var copy = (JsonObject)body.DeepClone();
        copy["model"] = agentSlug;
        if (stream)
        {
            copy["stream"] = true;
            var options = copy["stream_options"] as JsonObject ?? [];
            options["include_usage"] = true;
            copy["stream_options"] = options;
        }
        else copy["stream"] = false;
        return new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(copy.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    /// <summary><c>usage.total_tokens</c> of a JSON text, or 0 when there is none or the text is not JSON.</summary>
    internal static long TotalTokensOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object
                && u.TryGetProperty("total_tokens", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out var n) && n > 0 ? n : 0;
        }
        catch (JsonException) { return 0; }
    }
}
