using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Gabinety.Booking.Features.ApiKeys;
using Gabinety.Booking.Features.Internal.Erasure;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

public partial class ApiKeysApiTests
{
    // The in-memory database is shared by all hosts of a test run: every test gets its own users.
    readonly string Me = "demo-keys-" + Guid.NewGuid().ToString("N"), Other = "demo-keys-" + Guid.NewGuid().ToString("N");

    [GeneratedRegex("^gab_[0-9A-Za-z]{40}$")] private static partial Regex KeyShape();

    static Task<HttpResponseMessage> Create(CreateHost h, string user, string name = "demo-key") =>
        h.ClientFor(user).PostAsJsonAsync("/api/v1/me/api-keys", new { name });

    [Fact]
    public void Generate_gives_gab_plus_40_base62_characters_and_the_hash_is_lowercase_sha256_hex()
    {
        var a = ApiKeyHasher.Generate();
        Assert.Matches(KeyShape(), a);
        Assert.NotEqual(a, ApiKeyHasher.Generate());
        Assert.Equal(a.Substring(4, 8), ApiKeyHasher.PrefixOf(a));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ApiKeyHasher.Hash("abc"));
    }

    [Fact]
    public async Task Create_returns_the_key_once_and_the_database_holds_only_its_hash_and_prefix()
    {
        await using var h = new CreateHost();
        var r = await Create(h, Me, "demo-laptop");
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await r.Body();
        var key = j.GetProperty("key").GetString()!;
        Assert.Matches(KeyShape(), key);
        Assert.Equal("demo-laptop", j.GetProperty("name").GetString());
        Assert.Equal(key.Substring(4, 8), j.GetProperty("prefix").GetString());

        var row = await h.Query(db => db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == j.GetProperty("id").GetGuid()));
        Assert.Equal(Me, row.ClientUserId);
        Assert.Equal(ApiKeyHasher.Hash(key), row.Hash);
        Assert.Equal(key.Substring(4, 8), row.Prefix);
        Assert.DoesNotContain(key, string.Join("|", row.ClientUserId, row.Name, row.Prefix, row.Hash));
    }

    [Fact]
    public async Task List_shows_own_keys_without_the_key_and_hides_revoked_ones()
    {
        await using var h = new CreateHost();
        var kept = await (await Create(h, Me, "demo-kept")).Body();
        var gone = await (await Create(h, Me, "demo-gone")).Body();
        await Create(h, Other, "demo-theirs");
        Assert.Equal(HttpStatusCode.NoContent, (await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{gone.GetProperty("id").GetGuid()}")).StatusCode);

        var r = await h.ClientFor(Me).GetAsync("/api/v1/me/api-keys");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        var item = Assert.Single(System.Text.Json.JsonDocument.Parse(text).RootElement.EnumerateArray());
        Assert.Equal(kept.GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
        Assert.False(item.TryGetProperty("key", out _));
        Assert.DoesNotContain(kept.GetProperty("key").GetString()!, text);
    }

    [Fact]
    public async Task Revoking_sets_revoked_at_and_someone_elses_key_is_404()
    {
        await using var h = new CreateHost();
        var id = (await (await Create(h, Me)).Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await h.ClientFor(Other).DeleteAsync($"/api/v1/me/api-keys/{id}")).StatusCode);
        Assert.Null((await h.Query(db => db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == id))).RevokedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{Guid.NewGuid()}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{id}")).StatusCode);
        Assert.Equal(h.Clock.GetUtcNow(), (await h.Query(db => db.ApiKeys.AsNoTracking().SingleAsync(k => k.Id == id))).RevokedAt);
    }

    [Fact]
    public async Task The_sixth_active_key_is_422_validation_failed_and_a_revoked_key_frees_a_slot()
    {
        await using var h = new CreateHost();
        var first = Guid.Empty;
        for (var i = 0; i < ApiKeysFeature.MaxActive; i++)
        {
            var r = await Create(h, Me, $"demo-{i}");
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            if (i == 0) first = (await r.Body()).GetProperty("id").GetGuid();
        }
        var sixth = await Create(h, Me);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sixth.StatusCode);
        Assert.Equal("validation_failed", (await sixth.Body()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Create(h, Other)).StatusCode); // the limit is per user

        await h.ClientFor(Me).DeleteAsync($"/api/v1/me/api-keys/{first}");
        Assert.Equal(HttpStatusCode.Created, (await Create(h, Me)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_or_too_long_name_is_422(string name)
    {
        await using var h = new CreateHost();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(h, Me, name)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Create(h, Me, new string('x', 61))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Create(h, Me, new string('x', 60))).StatusCode);
    }

    [Fact]
    public async Task Without_a_token_the_routes_are_401()
    {
        await using var h = new CreateHost();
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.CreateClient().GetAsync("/api/v1/me/api-keys")).StatusCode);
    }

    [Fact]
    public async Task Erasing_a_user_deletes_their_keys_and_only_theirs()
    {
        await using var h = ErasureApiTests.NewHost();
        var user = ErasureApiTests.NewUser("client");
        await Create(h, user);
        await Create(h, Other);
        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h, user)).StatusCode);
        var left = await h.Query(db => db.ApiKeys.AsNoTracking().Where(k => k.ClientUserId == Other).ToListAsync());
        Assert.Equal(Other, Assert.Single(left).ClientUserId);
    }
}
