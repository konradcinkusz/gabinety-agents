using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Billing;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

/// <summary>A platform double that records what it was sent and answers as told.</summary>
sealed class SpyUpstream : IAgentUpstream
{
    public string Name => "Spy";
    public List<(string Slug, JsonObject Body)> Calls { get; } = [];
    public Func<UpstreamResult> Answer = () => new UpstreamResult(200, """{"object":"chat.completion","choices":[{"message":{"role":"assistant","content":"ok"}}],"usage":{"total_tokens":30}}""", 30);
    public Exception? Throw;

    public Task<UpstreamResult> CompleteAsync(string agentSlug, JsonObject body, CancellationToken ct)
    {
        Calls.Add((agentSlug, (JsonObject)body.DeepClone()));
        return Throw is null ? Task.FromResult(Answer()) : Task.FromException<UpstreamResult>(Throw);
    }

    public async Task<long> StreamAsync(string agentSlug, JsonObject body, Stream output, CancellationToken ct)
    {
        Calls.Add((agentSlug, (JsonObject)body.DeepClone()));
        if (Throw is not null) throw Throw;
        await output.WriteAsync("data: [DONE]\n\n"u8.ToArray(), ct);
        return 30;
    }
}

public class ChatApiTests
{
    const string Secret = "TRESC-WIADOMOSCI-NIE-DO-LOGOW";
    readonly string Me = AgentApiFx.NewUser(), Other = AgentApiFx.NewUser(), Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];

    static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    object Chat(string? model = null, bool? stream = null, string content = "test") =>
        stream is null ? new { model = model ?? Slug, messages = new[] { new { role = "user", content } } }
                       : new { model = model ?? Slug, stream, messages = new[] { new { role = "user", content } } };

    static async Task<JsonElement> Error(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    static Task<(UsageRow[] Rows, long Used)> State(CreateHost h, Guid package) => h.Query(async db => (
        await db.UsageRecords.AsNoTracking().Where(u => u.BookingId == package).Select(u => new UsageRow(u.Units, u.ApiKeyId, u.Kind)).ToArrayAsync(),
        (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package)).UnitsUsed));

    record UsageRow(long Units, Guid? ApiKeyId, OfferingKind Kind);

    // ---- authentication

    [Theory]
    [InlineData("GET", "/v1/models")]
    [InlineData("POST", "/v1/chat/completions")]
    public async Task Without_a_key_the_routes_are_401_invalid_api_key_in_the_OpenAI_shape(string method, string path)
    {
        await using var h = new CreateHost();
        var r = await h.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "POST" ? Json(Chat()) : null });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.StartsWith("application/json", r.Content.Headers.ContentType!.MediaType);
        var e = await Error(r);
        Assert.Equal("invalid_api_key", e.GetProperty("code").GetString());
        Assert.Equal(["message", "type", "code"], e.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task A_revoked_key_and_a_browser_token_are_401()
    {
        await using var h = new CreateHost();
        var (key, id) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Package(h, Me, Slug);
        Assert.Equal(HttpStatusCode.OK, (await AgentApiFx.ApiClient(h, key).GetAsync("/v1/models")).StatusCode);
        await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{id}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await AgentApiFx.ApiClient(h, key).GetAsync("/v1/models")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.ClientFor(Me).GetAsync("/v1/models")).StatusCode); // a login token is not an API key
    }

    // ---- models

    [Fact]
    public async Task Models_lists_the_agents_with_a_usable_chat_package_in_the_OpenAI_shape()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Package(h, Me, "demo-b-" + Slug);
        await AgentApiFx.Package(h, Me, "demo-a-" + Slug);
        await AgentApiFx.Package(h, Me, "demo-tasks-" + Slug, OfferingKind.Tasks, total: 3);
        await AgentApiFx.Package(h, Me, "demo-used-" + Slug, total: 10, used: 10);
        await AgentApiFx.Package(h, Other, "demo-theirs-" + Slug);

        var r = await AgentApiFx.ApiClient(h, key).GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("list", j.GetProperty("object").GetString());
        var data = j.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(["demo-a-" + Slug, "demo-b-" + Slug], data.Select(d => d.GetProperty("id").GetString()!).ToArray());
        Assert.All(data, d => { Assert.Equal("model", d.GetProperty("object").GetString()); Assert.Equal("gabinety", d.GetProperty("owned_by").GetString()); });
    }

    // ---- chat, not streamed

    [Fact]
    public async Task Without_a_package_the_model_is_404_model_not_found_and_a_tasks_package_does_not_count()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var api = AgentApiFx.ApiClient(h, key);
        var none = await api.PostAsync("/v1/chat/completions", Json(Chat()));
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("model_not_found", (await Error(none)).GetProperty("code").GetString());

        await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, total: 5);
        await AgentApiFx.Package(h, Other, Slug); // someone else's package
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/v1/chat/completions", Json(Chat()))).StatusCode);
    }

    [Fact]
    public async Task With_a_package_the_answer_is_the_platforms_and_UnitsUsed_grows_by_its_total_tokens()
    {
        await using var h = new CreateHost();
        var (key, keyId) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);

        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat(content: new string('x', 400))));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("chat.completion", j.GetProperty("object").GetString());
        Assert.Equal(120, j.GetProperty("usage").GetProperty("total_tokens").GetInt64()); // 400 / 4 + 20, the fake's rule

        var (rows, used) = await State(h, package);
        Assert.Equal(120, used);
        var row = Assert.Single(rows);
        Assert.Equal(new UsageRow(120, keyId, OfferingKind.Chat), row);
    }

    [Fact]
    public async Task A_package_with_tokens_left_is_served_then_overshoots_then_is_429()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug, total: 25, used: 0);
        var api = AgentApiFx.ApiClient(h, key);

        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync("/v1/chat/completions", Json(Chat()))).StatusCode); // 21 tokens: 4 left
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsync("/v1/chat/completions", Json(Chat()))).StatusCode); // served on 4 left, ends at 42 > 25
        Assert.Equal(42, (await State(h, package)).Used);

        var r = await api.PostAsync("/v1/chat/completions", Json(Chat()));
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        var e = await Error(r);
        Assert.Equal("insufficient_quota", e.GetProperty("code").GetString());
        Assert.Equal(42, (await State(h, package)).Used);
    }

    [Fact]
    public async Task The_next_package_is_used_when_the_oldest_is_used_up()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var first = await AgentApiFx.Package(h, Me, Slug, total: 10, used: 10, created: TimeSpan.FromMinutes(-5));
        var second = await AgentApiFx.Package(h, Me, Slug, total: 1000);
        Assert.Equal(HttpStatusCode.OK, (await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat()))).StatusCode);
        Assert.Equal(10, (await State(h, first)).Used);
        Assert.Equal(21, (await State(h, second)).Used);
    }

    [Fact]
    public async Task Tools_and_the_other_fields_reach_the_platform_unchanged_with_the_slug_of_the_package()
    {
        var spy = new SpyUpstream();
        await using var h = new CreateHost { Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(spy); } };
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Package(h, Me, Slug);
        var request = JsonNode.Parse("""
            {"model":"@@","temperature":0.2,"tools":[{"type":"function","function":{"name":"read_file","parameters":{"type":"object","properties":{"path":{"type":"string"}}}}}],
             "tool_choice":"auto","messages":[{"role":"user","content":"x"}],"x_custom":{"a":[1,2,3]}}
            """.Replace("@@", Slug))!.AsObject();

        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var (slug, body) = Assert.Single(spy.Calls);
        Assert.Equal(Slug, slug);
        Assert.True(JsonNode.DeepEquals(request, body), body.ToJsonString());
    }

    [Fact]
    public async Task A_platform_4xx_goes_back_as_it_came_and_costs_nothing()
    {
        var spy = new SpyUpstream { Answer = () => new UpstreamResult(400, """{"error":{"message":"bad tools","type":"invalid_request_error"}}""", 0) };
        await using var h = new CreateHost { Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(spy); } };
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);
        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("bad tools", (await Error(r)).GetProperty("message").GetString());
        var (rows, used) = await State(h, package);
        Assert.Empty(rows);
        Assert.Equal(0, used);
    }

    [Theory]
    [InlineData("exception")]
    [InlineData("status500")]
    [InlineData("status503")]
    public async Task A_platform_failure_is_502_upstream_error_and_nothing_is_consumed(string how)
    {
        var spy = new SpyUpstream();
        if (how == "exception") spy.Throw = new UpstreamException(0);
        else spy.Answer = () => new UpstreamResult(how == "status500" ? 500 : 503, "platform text", 0);
        await using var h = new CreateHost { Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(spy); } };
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);

        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat()));
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        var e = await Error(r);
        Assert.Equal("upstream_error", e.GetProperty("code").GetString());
        Assert.DoesNotContain("platform text", e.GetRawText());
        var (rows, used) = await State(h, package);
        Assert.Empty(rows);
        Assert.Equal(0, used);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    [InlineData("{\"messages\":[]}")]
    [InlineData("{\"model\":\"\",\"messages\":[]}")]
    [InlineData("{\"model\":5,\"messages\":[]}")]
    public async Task A_bad_body_or_a_missing_model_is_400_invalid_request_error(string body)
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("invalid_request_error", (await Error(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_body_over_2_MB_is_413()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Package(h, Me, Slug);
        var big = new StringContent("{\"model\":\"" + Slug + "\",\"messages\":[{\"role\":\"user\",\"content\":\"" + new string('x', 2 * 1024 * 1024) + "\"}]}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", big)).StatusCode);
    }

    // ---- chat, streamed

    [Fact]
    public async Task A_stream_is_event_stream_ends_with_DONE_and_the_usage_is_settled()
    {
        await using var h = new CreateHost();
        var (key, keyId) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);

        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat(stream: true, content: new string('x', 400))));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/event-stream", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-cache", r.Headers.CacheControl!.ToString());
        var text = await r.Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", text);
        Assert.Contains("\"total_tokens\":120", text);

        var (rows, used) = await State(h, package);
        Assert.Equal(120, used);
        Assert.Equal(new UsageRow(120, keyId, OfferingKind.Chat), Assert.Single(rows));
    }

    [Fact]
    public async Task A_stream_that_fails_before_the_first_byte_is_502_and_costs_nothing()
    {
        var spy = new SpyUpstream { Throw = new UpstreamException(503) };
        await using var h = new CreateHost { Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(spy); } };
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);
        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/chat/completions", Json(Chat(stream: true)));
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
        Assert.Equal("upstream_error", (await Error(r)).GetProperty("code").GetString());
        Assert.Equal(0, (await State(h, package)).Used);
    }

    [Fact]
    public async Task Stream_false_is_not_a_stream_and_a_string_stream_value_is_ignored()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Package(h, Me, Slug);
        var api = AgentApiFx.ApiClient(h, key);
        Assert.Equal("application/json", (await api.PostAsync("/v1/chat/completions", Json(Chat(stream: false)))).Content.Headers.ContentType!.MediaType);
        Assert.Equal("application/json", (await api.PostAsync("/v1/chat/completions", Json(new { model = Slug, stream = "true", messages = Array.Empty<object>() }))).Content.Headers.ContentType!.MediaType);
    }

    // ---- logs

    [Fact]
    public async Task Neither_the_messages_nor_the_key_nor_the_model_text_are_logged()
    {
        await using var h = new CreateHost();
        var (key, keyId) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug);
        var api = AgentApiFx.ApiClient(h, key);
        const string wrongModel = "model-TEKST-UZYTKOWNIKA";

        await api.PostAsync("/v1/chat/completions", Json(Chat(content: Secret)));
        await api.PostAsync("/v1/chat/completions", Json(Chat(stream: true, content: Secret)));
        await api.PostAsync("/v1/chat/completions", Json(Chat(model: wrongModel, content: Secret)));
        await api.PostAsync("/v1/chat/completions", new StringContent("{broken " + Secret, Encoding.UTF8, "application/json"));
        await AgentApiFx.ApiClient(h, key[..^1] + "x").GetAsync("/v1/models");

        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(Secret) || l.Contains(wrongModel) || l.Contains(key) || l.Contains(key[4..]));
        // what is logged: the package, the slug of the package, tokens, status
        Assert.Contains(h.Logs.Lines, l => l.Contains(package.ToString()) && l.Contains(Slug) && l.Contains("Tokens=27"));
    }

    // ---- rate limit

    [Fact]
    public async Task The_limit_is_per_api_key_and_a_refusal_is_429_rate_limit_exceeded_in_the_OpenAI_shape()
    {
        await using var h = new CreateHost();
        h.Settings["RateLimit:PerMinute"] = "3";
        var (k1, _) = await AgentApiFx.MakeKey(h, Me);
        var (k2, _) = await AgentApiFx.MakeKey(h, Me, "demo-second");
        var a = AgentApiFx.ApiClient(h, k1);

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/v1/models")).StatusCode);
        var refused = await a.GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/json", refused.Content.Headers.ContentType!.MediaType);
        var e = await Error(refused);
        Assert.Equal("rate_limit_exceeded", e.GetProperty("code").GetString());
        Assert.NotNull(refused.Headers.RetryAfter);

        Assert.Equal(HttpStatusCode.OK, (await AgentApiFx.ApiClient(h, k2).GetAsync("/v1/models")).StatusCode); // another key of the same client has its own window
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/v1/models")).StatusCode);
    }
}
