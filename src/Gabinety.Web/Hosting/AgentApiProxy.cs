using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Gabinety.Web.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Http;

namespace Gabinety.Web.Hosting;

/// <summary>
/// ADR-0013 decision 1 (owner: option B): the public agent API (<c>/v1/*</c>) is served under the address of the site, so a customer has ONE address. The Booking service still owns the API
/// (keys, packages, limits, the platform); this host only forwards. The rules, each pinned by a host test:
/// <list type="bullet">
/// <item>Forwarded request: the method, the path and query, the body STREAMED (never buffered; at most <see cref="MaxBodyBytes"/>, as in Booking), and exactly these headers: <c>Authorization</c>, <c>Idempotency-Key</c>,
/// <c>Content-Type</c>, <c>Accept</c>. Nothing else leaves: not the session cookie (the API is for non-browser clients authenticated by a key; the cookie is none of its business), not hop-by-hop headers.</item>
/// <item>The visitor's address goes in <c>X-Client-Address</c> with <c>X-Edge-Key</c> (the one <see cref="BackendHeadersHandler"/> does it, as for every backend call) so the limits that key on an address
/// (an anonymous or refused request) see the client and not this machine; the limit per key is Booking's and is unchanged.</item>
/// <item>The answer is streamed back without buffering (a server-sent stream flows as it arrives), with the provider's status and headers (<c>Retry-After</c>, <c>WWW-Authenticate</c>) except hop-by-hop headers and
/// <c>Set-Cookie</c>. When Booking does not answer: 502 in the OpenAI error shape.</item>
/// <item>Never logged: the body, the <c>Authorization</c> header, the content. Only method, status, duration and the exception type.</item>
/// <item>The route is outside the browser protections that do not apply to it (origin guard, antiforgery, cookie authentication, status-code pages, output cache): see <see cref="IsAgentApi"/> and <c>WebHostingExtensions</c>.</item>
/// </list>
/// </summary>
public static class AgentApiProxy
{
    public const string ClientName = "booking-v1", Prefix = "/v1";
    public const int MaxBodyBytes = 2 * 1024 * 1024; // as ChatCompletionsFeature.MaxBodyBytes in Booking

    static readonly string[] ForwardedRequestHeaders = ["Authorization", "Idempotency-Key", "Content-Type", "Accept"];

    // RFC 9110 section 7.6.1 plus the headers the host sets itself, and Set-Cookie: the API sets no session and a provider cookie must never reach a browser jar.
    static readonly HashSet<string> NotForwardedBack = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Set-Cookie", "Date", "Server",
    };

    /// <summary>True for <c>/v1</c> and everything under it (whole segments: <c>/v10</c> is not the API).</summary>
    public static bool IsAgentApi(PathString path) => path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);

    public static IServiceCollection AddAgentApiProxy(this IServiceCollection services)
    {
#pragma warning disable EXTEXP0001 // the one experimental call: Microsoft.Extensions.Http.Resilience has no stable way to opt one client out of the kernel's default handler
        services.AddHttpClient(ClientName, c =>
            {
                c.BaseAddress = new Uri(Backend.BaseAddress(Backend.Access));
                c.Timeout = TimeSpan.FromMinutes(15); // up to the response headers; the platform call itself is bounded by Agents:Upstream:TimeoutSeconds in Booking
            })
            .RemoveAllResilienceHandlers() // a streamed body cannot be replayed and a stream must not be cut at 15 s: no retry, no total timeout (the kernel's default handler)
            .AddHttpMessageHandler<BackendHeadersHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(5),
            });
#pragma warning restore EXTEXP0001
        return services;
    }

    public static IEndpointRouteBuilder MapAgentApiProxy(this IEndpointRouteBuilder endpoints)
    {
        // Every method (the routes are Booking's); anonymous because the API key, not a session, authenticates and Booking checks it; no antiforgery because no browser form posts here.
        endpoints.Map(Prefix + "/{**path}", ProxyAsync).WithMetadata(new AllowAnonymousAttribute()).DisableAntiforgery().ExcludeFromDescription();
        return endpoints;
    }

    static async Task ProxyAsync(HttpContext http, IHttpClientFactory clients, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("Gabinety.Web.AgentApiProxy");
        var request = http.Request;
        var ct = http.RequestAborted;
        var clock = Stopwatch.StartNew();
        if (http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } pages) pages.Enabled = false; // the answer is the provider's JSON (or ours), never the "not found" page

        if (HasDotSegment(request.Path))
        {
            await WriteErrorAsync(http, 404, "Not found.", "invalid_request_error", "not_found");
            return;
        }
        if (request.ContentLength > MaxBodyBytes)
        {
            await WriteErrorAsync(http, 413, "The request body is too large.", "invalid_request_error", "request_too_large");
            return;
        }

        using var outgoing = new HttpRequestMessage(new HttpMethod(request.Method), request.Path.ToUriComponent().TrimStart('/') + request.QueryString.ToUriComponent());
        var hasBody = request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding");
        if (hasBody)
        {
            outgoing.Content = new StreamContent(new LimitedStream(request.Body, MaxBodyBytes));
            if (request.ContentLength is { } length) outgoing.Content.Headers.ContentLength = length;
        }
        foreach (var name in ForwardedRequestHeaders)
        {
            if (!request.Headers.TryGetValue(name, out var values) || values.Count == 0) continue;
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (outgoing.Content is not null) outgoing.Content.Headers.TryAddWithoutValidation(name, values.OfType<string>().ToArray());
            }
            else outgoing.Headers.TryAddWithoutValidation(name, values.OfType<string>().ToArray());
        }
        if (ClientAddress.Of(http) is { } address) outgoing.Options.Set(RequestContext.VisitorAddress, address);

        HttpResponseMessage response;
        try
        {
            response = await clients.CreateClient(ClientName).SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; } // the client left
        catch (Exception ex) when (IsBodyTooLarge(ex))
        {
            await WriteErrorAsync(http, 413, "The request body is too large.", "invalid_request_error", "request_too_large");
            return;
        }
        catch (Exception ex)
        {
            log.LogWarning("v1 proxy: {Method} no answer from the API service after {Ms} ms ({Error})", request.Method, clock.ElapsedMilliseconds, ex.GetType().Name);
            await WriteErrorAsync(http, 502, "The API service did not answer.", "api_error", "upstream_error");
            return;
        }

        using (response)
        {
            http.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!NotForwardedBack.Contains(header.Key)) http.Response.Headers[header.Key] = header.Value.ToArray();
            http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
            try
            {
                await http.Response.StartAsync(ct);
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[8192];
                int read;
                while ((read = await body.ReadAsync(buffer, ct)) > 0)
                {
                    await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                    await http.Response.Body.FlushAsync(ct); // a server-sent event leaves now, not when the buffer fills
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { } // the client left: stop reading, Booking sees the connection close
            catch (Exception ex)
            {
                // The status line is already out: the only honest signal left is a broken connection.
                log.LogWarning("v1 proxy: {Method} stream broke after {Ms} ms ({Error})", request.Method, clock.ElapsedMilliseconds, ex.GetType().Name);
                http.Abort();
                return;
            }
            log.LogInformation("v1 proxy: {Method} {Status} in {Ms} ms", request.Method, (int)response.StatusCode, clock.ElapsedMilliseconds);
        }
    }

    static bool HasDotSegment(PathString path) =>
        (path.Value ?? "").Split('/').Any(s => s is "." or ".." || s.Contains('\\'));

    static bool IsBodyTooLarge(Exception ex) => ex is BodyTooLargeException || ex.InnerException is { } inner && IsBodyTooLarge(inner);

    static Task WriteErrorAsync(HttpContext http, int status, string message, string type, string code)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/json";
        return http.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { message, type, code } }), http.RequestAborted);
    }

    sealed class BodyTooLargeException : IOException;

    /// <summary>Counts what the client sends and fails past the limit, so a body without a length (chunked) is held to the same 2 MB.</summary>
    sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        long _seen;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            _seen += n;
            if (_seen > limit) throw new BodyTooLargeException();
            return n;
        }
    }
}
