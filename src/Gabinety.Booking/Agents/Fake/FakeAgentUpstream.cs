using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Booking.Agents.Abstractions;

namespace Gabinety.Booking.Agents.Fake;

/// <summary>
/// The fake profile's platform (<c>Agents:Provider=Fake</c>): no network, deterministic. The answer is <c>[demo &lt;slug&gt;] Otrzymano N znaków.</c>, where N is the total length of
/// the message contents; the content itself is never echoed. <c>TotalTokens</c> = N / 4 + 20.
/// </summary>
public sealed class FakeAgentUpstream(TimeProvider time) : IAgentUpstream
{
    public string Name => "Fake";

    public Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct)
    {
        var n = ContentLength(body);
        var tokens = Tokens(n);
        var created = time.GetUtcNow().ToUnixTimeSeconds();
        var json = new JsonObject
        {
            ["id"] = "chatcmpl-demo", ["object"] = "chat.completion", ["created"] = created, ["model"] = agentSlug,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = Answer(agentSlug, n) },
                ["finish_reason"] = "stop",
            }),
            ["usage"] = Usage(tokens),
        };
        return Task.FromResult(new UpstreamResult(200, json.ToJsonString(), tokens));
    }

    public async Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct)
    {
        var n = ContentLength(body);
        var tokens = Tokens(n);
        var created = time.GetUtcNow().ToUnixTimeSeconds();
        string[] parts = [$"[demo {agentSlug}]", $" Otrzymano {n}", " znaków."];
        for (var i = 0; i < parts.Length; i++)
        {
            var delta = new JsonObject { ["content"] = parts[i] };
            if (i == 0) delta["role"] = "assistant";
            await WriteAsync(output, Chunk(agentSlug, created, new JsonArray(new JsonObject
            {
                ["index"] = 0, ["delta"] = delta, ["finish_reason"] = i == parts.Length - 1 ? "stop" : null,
            }), null), ct);
        }
        await WriteAsync(output, Chunk(agentSlug, created, new JsonArray(), Usage(tokens)), ct); // the usage chunk: no choices
        await output.WriteAsync("data: [DONE]\n\n"u8.ToArray(), ct);
        await output.FlushAsync(ct);
        return tokens;
    }

    static string Answer(string slug, int n) => $"[demo {slug}] Otrzymano {n} znaków.";

    static long Tokens(int n) => n / 4 + 20;

    static JsonObject Usage(long total) => new() { ["prompt_tokens"] = total - 10, ["completion_tokens"] = 10, ["total_tokens"] = total };

    static JsonObject Chunk(string slug, long created, JsonArray choices, JsonObject? usage)
    {
        var o = new JsonObject { ["id"] = "chatcmpl-demo", ["object"] = "chat.completion.chunk", ["created"] = created, ["model"] = slug, ["choices"] = choices };
        if (usage is not null) o["usage"] = usage;
        return o;
    }

    static async Task WriteAsync(Stream output, JsonObject chunk, CancellationToken ct)
    {
        await output.WriteAsync(Encoding.UTF8.GetBytes($"data: {chunk.ToJsonString()}\n\n"), ct);
        await output.FlushAsync(ct);
    }

    /// <summary>The total number of characters in <c>messages[].content</c>: a string, or the <c>text</c> of each part of an array. Other shapes count as 0.</summary>
    internal static int ContentLength(JsonObject body)
    {
        if (body["messages"] is not JsonArray messages) return 0;
        var n = 0;
        foreach (var m in messages)
        {
            var content = (m as JsonObject)?["content"];
            if (content is JsonValue v && v.TryGetValue<string>(out var s)) n += s.Length;
            else if (content is JsonArray parts)
                foreach (var p in parts)
                    if ((p as JsonObject)?["text"] is JsonValue t && t.TryGetValue<string>(out var ts)) n += ts.Length;
        }
        return n;
    }
}
