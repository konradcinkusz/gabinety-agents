using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Booking.Agents;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Booking.Agents.Fake;
using Gabinety.Booking.Agents.Http;
using Gabinety.Booking.Tests.StripePayments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

public class AgentUpstreamTests
{
    const string Slug = "demo-bartosz-testowy";
    const string Secret = "TEKST-UZYTKOWNIKA-NIE-DO-ODPOWIEDZI";

    static JsonObject Request(params string[] contents) => new()
    {
        ["model"] = "ignored",
        ["messages"] = new JsonArray(contents.Select(c => (JsonNode)new JsonObject { ["role"] = "user", ["content"] = c }).ToArray()),
    };

    static FakeAgentUpstream Fake() => new(new FixedClock(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)));

    // ---- fake

    [Fact]
    public async Task Fake_returns_a_chat_completion_with_the_character_count_and_never_echoes_the_content()
    {
        var r = await Fake().CompleteAsync(Slug, Request(Secret, "abc"), default);
        Assert.Equal(200, r.StatusCode);
        var j = JsonNode.Parse(r.Json)!;
        Assert.Equal("chat.completion", (string?)j["object"]);
        Assert.Equal(Slug, (string?)j["model"]);
        Assert.Equal($"[demo {Slug}] Otrzymano {Secret.Length + 3} znaków.", (string?)j["choices"]![0]!["message"]!["content"]);
        Assert.Equal("assistant", (string?)j["choices"]![0]!["message"]!["role"]);
        Assert.DoesNotContain(Secret, r.Json);
        Assert.Equal((Secret.Length + 3) / 4 + 20, r.TotalTokens);
        Assert.Equal(r.TotalTokens, (long?)j["usage"]!["total_tokens"]);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(3, 20)]
    [InlineData(4, 21)]
    [InlineData(400, 120)]
    public async Task Fake_tokens_are_length_over_4_plus_20_and_repeat(int length, long expected)
    {
        var body = Request(new string('x', length));
        Assert.Equal(expected, (await Fake().CompleteAsync(Slug, body, default)).TotalTokens);
        Assert.Equal(expected, (await Fake().CompleteAsync(Slug, body, default)).TotalTokens);
    }

    [Fact]
    public async Task Fake_counts_text_parts_of_array_content_and_ignores_other_shapes()
    {
        var body = new JsonObject
        {
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "abcd" }, new JsonObject { ["type"] = "image_url" }) },
                new JsonObject { ["role"] = "assistant", ["content"] = null }),
        };
        var r = await Fake().CompleteAsync(Slug, body, default);
        Assert.Equal($"[demo {Slug}] Otrzymano 4 znaków.", (string?)JsonNode.Parse(r.Json)!["choices"]![0]!["message"]!["content"]);
    }

    [Fact]
    public async Task Fake_stream_sends_three_chunks_a_usage_chunk_and_done()
    {
        using var ms = new MemoryStream();
        var tokens = await Fake().StreamAsync(Slug, Request(Secret), ms, default);
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.EndsWith("data: [DONE]\n\n", text);
        Assert.DoesNotContain(Secret, text);

        var events = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(e => e["data: ".Length..]).ToList();
        Assert.Equal("[DONE]", events[^1]);
        var chunks = events[..^1].Select(e => JsonNode.Parse(e)!).ToList();
        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, c => Assert.Equal("chat.completion.chunk", (string?)c["object"]));
        var content = string.Concat(chunks.Take(3).Select(c => (string?)c["choices"]![0]!["delta"]!["content"]));
        Assert.Equal($"[demo {Slug}] Otrzymano {Secret.Length} znaków.", content);
        Assert.Empty(chunks[3]["choices"]!.AsArray());
        Assert.Equal(tokens, (long?)chunks[3]["usage"]!["total_tokens"]);
        Assert.Equal(Secret.Length / 4 + 20, tokens);
    }

    // ---- http

    sealed class Stub(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Last; public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return answer(request, LastBody);
        }
    }

    static HttpAgentUpstream Http(Stub stub) =>
        new(new HttpClient(stub) { BaseAddress = new Uri("https://agents.demo.invalid/") });

    static HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Http_posts_to_v1_chat_completions_replaces_the_model_and_keeps_the_other_fields()
    {
        var stub = new Stub((_, _) => Json(HttpStatusCode.OK, """{"id":"x","usage":{"total_tokens":42}}"""));
        var body = Request("hi");
        body["tools"] = new JsonArray(new JsonObject { ["type"] = "function" });
        var r = await Http(stub).CompleteAsync(Slug, body, default);

        Assert.Equal("https://agents.demo.invalid/v1/chat/completions", stub.Last!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, stub.Last.Method);
        var sent = JsonNode.Parse(stub.LastBody!)!;
        Assert.Equal(Slug, (string?)sent["model"]);
        Assert.Equal(false, (bool?)sent["stream"]);
        Assert.Equal("function", (string?)sent["tools"]![0]!["type"]);
        Assert.Equal("ignored", (string?)body["model"]); // the caller's body is not changed
        Assert.Equal((200, 42L), (r.StatusCode, r.TotalTokens));
        Assert.Contains("\"id\":\"x\"", r.Json);
    }

    [Fact]
    public async Task Http_returns_an_error_status_and_its_body_as_they_came_with_zero_tokens()
    {
        var stub = new Stub((_, _) => Json(HttpStatusCode.BadRequest, """{"error":{"message":"bad","usage":{"total_tokens":9}}}"""));
        var r = await Http(stub).CompleteAsync(Slug, Request("hi"), default);
        Assert.Equal((400, 0L), (r.StatusCode, r.TotalTokens));
        Assert.Contains("\"bad\"", r.Json);
    }

    [Fact]
    public async Task Http_failing_to_connect_is_an_UpstreamException_without_a_text_of_the_user()
    {
        var stub = new Stub((_, _) => throw new HttpRequestException(Secret));
        var e = await Assert.ThrowsAsync<UpstreamException>(() => Http(stub).CompleteAsync(Slug, Request(Secret), default));
        Assert.Equal(0, e.StatusCode);
        Assert.DoesNotContain(Secret, e.Message);
    }

    [Fact]
    public async Task Http_stream_forces_include_usage_copies_the_lines_and_returns_the_tokens_of_the_last_usage_chunk()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}\n\n" +
                  "data: {\"choices\":[],\"usage\":{\"total_tokens\":77}}\n\n" +
                  "data: [DONE]\n\n";
        var stub = new Stub((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") });
        var body = Request("hi");
        body["stream_options"] = new JsonObject { ["other"] = 1 };
        using var ms = new MemoryStream();
        var tokens = await Http(stub).StreamAsync(Slug, body, ms, default);

        Assert.Equal(77, tokens);
        Assert.Equal(sse, Encoding.UTF8.GetString(ms.ToArray()));
        var sent = JsonNode.Parse(stub.LastBody!)!;
        Assert.Equal(true, (bool?)sent["stream"]);
        Assert.Equal(true, (bool?)sent["stream_options"]!["include_usage"]);
        Assert.Equal(1, (int?)sent["stream_options"]!["other"]);
        Assert.Equal(Slug, (string?)sent["model"]);
    }

    [Fact]
    public async Task Http_stream_with_an_error_status_throws_before_writing_anything()
    {
        var stub = new Stub((_, _) => Json(HttpStatusCode.InternalServerError, Secret));
        using var ms = new MemoryStream();
        var e = await Assert.ThrowsAsync<UpstreamException>(() => Http(stub).StreamAsync(Slug, Request("hi"), ms, default));
        Assert.Equal(500, e.StatusCode);
        Assert.Equal(0, ms.Length);
        Assert.DoesNotContain(Secret, e.Message);
    }

    // ---- registration

    static IConfiguration Config(params (string, string?)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2))).Build();

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("Fake", true, false)]
    [InlineData(null, false, true)]
    [InlineData("Fake", false, true)]
    [InlineData("Nope", true, true)]
    public void Problem_for_fake_and_unknown_providers(string? provider, bool nonDeployed, bool refused) =>
        Assert.Equal(refused, AgentsFeature.Problem(provider, nonDeployed, Config()) is not null);

    [Theory]
    [InlineData(null, "k", true)]
    [InlineData("not a url", "k", true)]
    [InlineData("ftp://x.invalid", "k", true)]
    [InlineData("https://x.invalid", null, true)]
    [InlineData("https://x.invalid", "  ", true)]
    [InlineData("https://x.invalid", "k", false)]
    public void Http_needs_a_base_url_and_a_key_and_the_message_never_holds_the_key(string? url, string? key, bool refused)
    {
        var p = AgentsFeature.Problem("Http", false, Config(("Agents:Upstream:BaseUrl", url), ("Agents:Upstream:ApiKey", key)));
        Assert.Equal(refused, p is not null);
        if (key is { Length: > 1 }) Assert.DoesNotContain(key, p ?? "");
    }

    static ServiceProvider Build(params (string, string?)[] pairs)
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Item1, p.Item2)));
        new AgentsFeature().Add(builder);
        builder.Services.AddSingleton(TimeProvider.System);
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void The_fake_profile_resolves_the_fake_and_Http_resolves_the_http_upstream()
    {
        using var fake = Build(("GABINETY_FAKE", "1"), ("Agents:Provider", "Fake"));
        Assert.Equal("Fake", fake.GetRequiredService<IAgentUpstream>().Name);
        using var fakeDefault = Build(("GABINETY_FAKE", "1"));
        Assert.Equal("Fake", fakeDefault.GetRequiredService<IAgentUpstream>().Name);
        using var http = Build(("Agents:Provider", "Http"), ("Agents:Upstream:BaseUrl", "https://agents.demo.invalid"), ("Agents:Upstream:ApiKey", "demo-key"));
        Assert.Equal("Http", http.GetRequiredService<IAgentUpstream>().Name);
    }

    [Fact]
    public void A_deployed_profile_without_a_provider_throws_only_when_the_upstream_is_resolved()
    {
        using var sp = Build();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IAgentUpstream>());
    }
}
