using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using static Gabinety.Directory.Tests.Packages.Kit;

namespace Gabinety.Directory.Tests.Packages;

/// <summary>A-01: an offering is a package with a kind and a number of units.</summary>
public class PackageKindTests(PackageFactory f) : IClassFixture<PackageFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await WaitForSeed(f);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Chat_package_returns_kind_and_units()
    {
        var c = await Agent(f);
        var created = await Created(c, Valid() with { Kind = PackageKind.Chat, Units = 500 });
        Assert.Equal(PackageKind.Chat, created.Kind);
        Assert.Equal(500, created.Units);
        var listed = Assert.Single(await List(c));
        Assert.Equal(PackageKind.Chat, listed.Kind);
        Assert.Equal(500, listed.Units);
    }

    [Fact]
    public async Task Request_without_kind_defaults_to_tasks_and_one_unit()
    {
        var req = JsonSerializer.Deserialize<PackageRequest>(
            """{"title":"Przegląd kodu .NET","description":"","durationMinutes":30,"priceMinor":10000,"problemSlug":"code-review","techSlugs":["dotnet"]}""", J)!;
        Assert.Equal(PackageKind.Tasks, req.Kind);
        Assert.Equal(1, req.Units);

        var c = await Agent(f);
        var created = await Created(c, req);
        Assert.Equal(PackageKind.Tasks, created.Kind);
        Assert.Equal(1, created.Units);
    }

    [Theory]
    [InlineData(PackageKind.Tasks, 0, false)] [InlineData(PackageKind.Tasks, 1, true)] [InlineData(PackageKind.Tasks, 1000, true)] [InlineData(PackageKind.Tasks, 1001, false)]
    [InlineData(PackageKind.Chat, 99, false)] [InlineData(PackageKind.Chat, 100, true)] [InlineData(PackageKind.Chat, 100000, true)] [InlineData(PackageKind.Chat, 100001, false)]
    public async Task Units_range_depends_on_kind(PackageKind kind, int units, bool ok)
    {
        var c = await Agent(f);
        var r = await c.PostAsJsonAsync(Url, Valid() with { Kind = kind, Units = units });
        if (ok) { Assert.Equal(HttpStatusCode.Created, r.StatusCode); return; }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Contains("units", (await ProblemOf(r)).Errors!.Keys);
        Assert.Empty(await List(c));
    }

    [Fact]
    public async Task Update_replaces_kind_and_units()
    {
        var c = await Agent(f);
        var o = await Created(c, Valid());
        var r = await c.PutAsJsonAsync($"{Url}/{o.Id}", Valid() with { Kind = PackageKind.Chat, Units = 1000 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var updated = (await r.Content.ReadFromJsonAsync<PackagePrivate>(J))!;
        Assert.Equal((PackageKind.Chat, 1000), (updated.Kind, updated.Units));
    }
}
