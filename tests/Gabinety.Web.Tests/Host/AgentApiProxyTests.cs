using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Gabinety.Web.Tests.Host;

/// <summary>
/// ADR-0013 decision 1: <c>/v1/*</c> is forwarded to Booking under the site's address. What is forwarded, what never is, that a stream flows, the 2 MB limit, the visitor address for the limits,
/// 502 when Booking is down, and that no browser protection (origin guard, antiforgery, cookie authentication, status pages) is in the way.
/// </summary>
public class AgentApiProxyTests
{
    const string Key = "gab_DEMOKEYDEMOKEYDEMOKEYDEMOKEYDEMOKEYDEMO";

    sealed record Seen(string Method, string Uri, Dictionary<string, string> Headers, string Body);

    static (WebFactory Web, List<Seen> Seen) Host(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var web = new WebFactory();
        var seen = new List<Seen>();
        web.Access.Respond = r =>
        {
            lock (seen)
                seen.Add(new(r.Method.Method, r.RequestUri!.PathAndQuery,
                    r.Headers.Concat(r.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()).ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), ""));
            return (respond ?? (_ => FakeBackend.Raw("{\"ok\":true}")))(r);
        };
        return (web, seen);
    }

    static HttpRequestMessage Post(string path, string body, params (string, string)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (n, v) in headers) request.Headers.TryAddWithoutValidation(n, v);
        return request;
    }

    [Fact]
    public async Task The_method_path_query_body_and_the_four_headers_reach_Booking_and_the_answer_comes_back_as_it_is()
    {
        var (web, seen) = Host(r => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"id\":\"1\",\"status\":\"queued\"}", Encoding.UTF8, "application/json"),
            Headers = { { "Retry-After", "7" }, { "X-Provider", "yes" } },
        });
        await using var _ = web;
        var client = web.CreateClient();
        var response = await client.SendAsync(Post("/v1/tasks?x=1", "{\"model\":\"demo\"}", ("Authorization", $"Bearer {Key}"), ("Idempotency-Key", "abc-123"), ("Accept", "application/json"), ("X-Other", "dropped"), ("X-Forwarded-For", "9.9.9.9")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{\"id\":\"1\",\"status\":\"queued\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("7", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal("yes", response.Headers.GetValues("X-Provider").Single());
        var call = Assert.Single(web.Access.Requests);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/v1/tasks?x=1", call.PathAndQuery);
        Assert.Equal($"Bearer {Key}", call.Authorization);
        Assert.Equal("{\"model\":\"demo\"}", call.Body);
        var headers = Assert.Single(seen).Headers;
        Assert.Equal("abc-123", headers["Idempotency-Key"]);
        Assert.Equal("application/json", headers["Accept"]);
        Assert.StartsWith("application/json", headers["Content-Type"]);
        Assert.DoesNotContain("X-Other", headers.Keys); // nothing outside the list leaves
    }

    [Fact]
    public async Task The_session_cookie_and_every_other_cookie_never_reach_Booking_and_the_answers_cookie_never_reaches_the_client()
    {
        var (web, seen) = Host(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}"), Headers = { { "Set-Cookie", "provider=1; Path=/" } } });
        await using var _ = web;
        var request = Post("/v1/chat/completions", "{}", ("Authorization", $"Bearer {Key}"), ("Cookie", "gt_session=SECRET-SESSION; other=1"));
        var response = await web.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var headers = Assert.Single(seen).Headers;
        Assert.DoesNotContain("Cookie", headers.Keys);
        Assert.DoesNotContain(headers.Values, v => v.Contains("SECRET-SESSION"));
    }

    [Fact]
    public async Task A_session_cookie_does_not_turn_the_API_key_into_a_session_and_a_missing_key_is_the_providers_401_not_a_sign_in_redirect()
    {
        var (web, _) = Host(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"code\":\"invalid_api_key\"}}", Encoding.UTF8, "application/json"),
            Headers = { { "WWW-Authenticate", "Bearer" } },
        });
        await using var _ = web;
        var response = await web.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Contains("invalid_api_key", await response.Content.ReadAsStringAsync()); // the provider's body, not the "not found" page the status-code middleware would put there
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task The_browser_origin_guard_and_antiforgery_do_not_apply_to_v1_but_still_do_to_the_pages()
    {
        var (web, _) = Host();
        await using var _ = web;
        var client = web.CreateClient(new() { AllowAutoRedirect = false });
        // A non-browser client may send what a browser would not (here a foreign Origin and a cross-site fetch): the API is authenticated by the key, so the guard is not its business.
        var api = await client.SendAsync(Post("/v1/chat/completions", "{}", ("Authorization", $"Bearer {Key}"), ("Origin", "https://evil.example"), ("Sec-Fetch-Site", "cross-site")));
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        // ... and the same headers on a page or the circuit are still refused.
        var page = await client.SendAsync(Post("/_blazor/negotiate?negotiateVersion=1", "", ("Origin", "https://evil.example")));
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        // A POST without an antiforgery token is 200 here (no token exists for the API) and does not run the form pipeline.
        var noToken = await client.SendAsync(Post("/v1/tasks", "{}"));
        Assert.Equal(HttpStatusCode.OK, noToken.StatusCode);
    }

    [Fact]
    public async Task A_response_is_streamed_each_chunk_leaves_before_the_provider_has_finished()
    {
        var gate = new TaskCompletionSource();
        var (web, _) = Host(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = Gated(gate.Task, "data: one\n\n", "data: two\n\n") });
        await using var _ = web;
        using var response = await web.CreateClient().SendAsync(Post("/v1/chat/completions", "{\"stream\":true}", ("Authorization", $"Bearer {Key}")), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(body);
        var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)); // the provider is still holding the rest back
        Assert.Equal("data: one", first);
        Assert.False(gate.Task.IsCompleted);
        gate.SetResult();
        var rest = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("data: two", rest);
    }

    [Fact]
    public async Task A_body_over_2_MB_is_413_in_the_OpenAI_shape_and_is_not_forwarded()
    {
        var (web, _) = Host();
        await using var _ = web;
        var request = Post("/v1/chat/completions", new string('a', 2 * 1024 * 1024 + 1), ("Authorization", $"Bearer {Key}"));
        var response = await web.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request_too_large", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(web.Access.Requests);
    }

    [Fact]
    public async Task A_body_with_no_length_over_2_MB_is_stopped_while_it_is_streamed()
    {
        var (web, _) = Host(r => { r.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult(); return FakeBackend.Raw("{}"); });
        await using var _ = web;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions") { Content = new ChunkedContent(3 * 1024 * 1024) };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Key}");
        var response = await web.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task The_visitor_address_goes_to_Booking_with_the_proxy_key_and_the_visitors_own_values_are_never_copied()
    {
        var (web, _) = Host();
        await using var _ = web;
        var request = Post("/v1/models", "{}", ("Authorization", $"Bearer {Key}"), ("Fly-Client-IP", "203.0.113.9"), ("X-Client-Address", "1.2.3.4"), ("X-Edge-Key", "guess"));
        await web.CreateClient().SendAsync(request);
        var call = Assert.Single(web.Access.Requests);
        Assert.Equal("203.0.113.9", call.ClientAddress);
        Assert.Equal(WebFactory.TrustedKey, call.EdgeKey);
    }

    [Fact]
    public async Task When_Booking_does_not_answer_the_client_gets_502_in_the_OpenAI_shape_without_the_exception_text()
    {
        var (web, _) = Host(FakeBackend.Throw(new HttpRequestException("connect ECONNREFUSED 10.0.0.7:8080 SECRET-HOST")));
        await using var _ = web;
        var response = await web.CreateClient().SendAsync(Post("/v1/tasks", "{}", ("Authorization", $"Bearer {Key}")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("upstream_error", JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("SECRET-HOST", body);
    }

    [Fact]
    public async Task Nothing_the_client_sent_is_logged()
    {
        var sink = new Sink();
        var (web, _) = Host();
        await using var _ = web.LogTo(sink);
        await web.CreateClient().SendAsync(Post("/v1/chat/completions", "{\"messages\":[{\"content\":\"BODY-TEXT-MUST-NOT-BE-LOGGED\"}]}", ("Authorization", $"Bearer {Key}")));
        Assert.NotEmpty(sink.Lines);
        Assert.DoesNotContain(sink.Lines, l => l.Contains("BODY-TEXT") || l.Contains(Key) || l.Contains("Bearer"));
    }

    [Theory]
    [InlineData("/v1/../internal/v1/demo/reset")]
    [InlineData("/v1/%2e%2e/api/v1/admin/practitioners")]
    public async Task A_path_that_climbs_out_of_v1_is_never_forwarded(string path)
    {
        var (web, _) = Host();
        await using var _ = web;
        var response = await web.CreateClient(new() { AllowAutoRedirect = false }).GetAsync(path);
        Assert.Empty(web.Access.Requests);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Only_whole_segments_of_v1_are_the_API()
    {
        var (web, _) = Host();
        await using var _ = web;
        var response = await web.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/v10/models");
        Assert.Empty(web.Access.Requests);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    sealed class Sink : ILoggerProvider, ILogger
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string category) => category.Contains("AgentApiProxy") ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { lock (Lines) Lines.Add(formatter(state, exception)); }
        public void Dispose() { }
    }

    /// <summary>The first chunk now, the rest when the gate opens: what a provider's event stream does. A pipe, because HttpContent subclasses are buffered whole by ReadAsStreamAsync.</summary>
    static HttpContent Gated(Task gate, params string[] chunks)
    {
        var pipe = new System.IO.Pipelines.Pipe();
        _ = Task.Run(async () =>
        {
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(chunks[0]));
            await gate;
            foreach (var c in chunks.Skip(1)) await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(c));
            await pipe.Writer.CompleteAsync();
        });
        var content = new StreamContent(pipe.Reader.AsStream());
        content.Headers.ContentType = new("text/event-stream");
        return content;
    }

    /// <summary>A body with no Content-Length (chunked): the proxy cannot refuse it up front.</summary>
    sealed class ChunkedContent(int size) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            for (var sent = 0; sent < size; sent += chunk.Length) await stream.WriteAsync(chunk);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
