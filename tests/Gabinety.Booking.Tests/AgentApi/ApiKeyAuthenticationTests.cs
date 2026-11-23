using System.Text.Json;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

public class ApiKeyAuthenticationTests
{
    readonly string Me = AgentApiFx.NewUser();

    [Fact]
    public async Task A_valid_key_signs_in_with_sub_and_api_key_id()
    {
        await using var h = new CreateHost();
        var (key, id) = await AgentApiFx.MakeKey(h, Me);
        var (result, _) = await AgentApiFx.Authenticate(h, "Bearer " + key);
        Assert.True(result.Succeeded);
        Assert.Equal(Me, result.Principal!.FindFirst("sub")!.Value);
        Assert.Equal(id.ToString(), result.Principal.FindFirst("api_key_id")!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer not-a-key")]
    [InlineData("Bearer gab_short")]
    public async Task No_header_or_a_header_that_is_not_a_gab_key_is_no_result(string? header)
    {
        await using var h = new CreateHost();
        var (result, _) = await AgentApiFx.Authenticate(h, header);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_well_formed_but_unknown_key_is_no_result()
    {
        await using var h = new CreateHost();
        var (result, _) = await AgentApiFx.Authenticate(h, "Bearer " + Gabinety.Booking.Features.ApiKeys.ApiKeyHasher.Generate());
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_revoked_key_is_no_result()
    {
        await using var h = new CreateHost();
        var (key, id) = await AgentApiFx.MakeKey(h, Me);
        Assert.True((await AgentApiFx.Authenticate(h, "Bearer " + key)).Result.Succeeded);
        Assert.True((await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{id}")).IsSuccessStatusCode);
        Assert.False((await AgentApiFx.Authenticate(h, "Bearer " + key)).Result.Succeeded);
    }

    [Fact]
    public async Task The_scheme_name_of_the_header_is_case_insensitive()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        Assert.True((await AgentApiFx.Authenticate(h, "bearer " + key)).Result.Succeeded);
    }

    [Fact]
    public async Task The_challenge_is_401_invalid_api_key_in_the_OpenAI_shape()
    {
        await using var h = new CreateHost();
        var (_, ctx) = await AgentApiFx.Authenticate(h, null);
        await ctx.ChallengeAsync("ApiKey");
        Assert.Equal(401, ctx.Response.StatusCode);
        Assert.StartsWith("application/json", ctx.Response.ContentType);
        ctx.Response.Body.Position = 0;
        var error = JsonDocument.Parse(ctx.Response.Body).RootElement.GetProperty("error");
        Assert.Equal("invalid_api_key", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task LastUsedAt_moves_at_most_once_a_minute()
    {
        await using var h = new CreateHost();
        var (key, id) = await AgentApiFx.MakeKey(h, Me);
        async Task<DateTimeOffset?> Last() => (await h.Query(db => db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == id))).LastUsedAt;

        Assert.Null(await Last());
        var first = h.Clock.GetUtcNow();
        await AgentApiFx.Authenticate(h, "Bearer " + key);
        Assert.Equal(first, await Last());

        h.Clock.Advance(TimeSpan.FromSeconds(59));
        await AgentApiFx.Authenticate(h, "Bearer " + key);
        Assert.Equal(first, await Last());

        h.Clock.Advance(TimeSpan.FromSeconds(1));
        await AgentApiFx.Authenticate(h, "Bearer " + key);
        Assert.Equal(h.Clock.GetUtcNow(), await Last());
    }

    [Fact]
    public async Task Authenticating_never_writes_the_key_to_the_log()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        await AgentApiFx.Authenticate(h, "Bearer " + key);
        var (_, ctx) = await AgentApiFx.Authenticate(h, "Bearer " + key[..^1] + "x");
        await ctx.ChallengeAsync("ApiKey");
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains(key[4..], StringComparison.Ordinal) || l.Contains(key[..^1], StringComparison.Ordinal));
    }

    // ---- what B-02 adds around the scheme

    [Theory]
    [InlineData("/v1/models", true)]
    [InlineData("/v1/chat/completions", true)]
    [InlineData("/V1/tasks/abc", true)]
    [InlineData("/api/v1/me/bookings", true)]
    [InlineData("/api/v1/admin/disputes", false)]
    [InlineData("/internal/v1/subjects/x", false)]
    [InlineData("/v10/models", false)]
    public void The_public_edge_serves_the_agent_api(string path, bool served)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "GET";
        Assert.Equal(served, PublicEdge.Serves(ctx.Request));
    }

    [Fact]
    public void The_error_body_has_exactly_message_type_and_code()
    {
        var error = JsonDocument.Parse(OpenAiError.Body("m", "t", "c")).RootElement.GetProperty("error");
        Assert.Equal(["message", "type", "code"], error.EnumerateObject().Select(p => p.Name).ToArray());
    }
}
