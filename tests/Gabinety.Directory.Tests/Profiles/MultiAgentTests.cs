using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Verification;
using Gabinety.Directory.Tests.Internal;
using Gabinety.Directory.Tests.Offerings;
using Gabinety.Directory.Tests.Verification;
using static Gabinety.Directory.Tests.Offerings.Kit;

namespace Gabinety.Directory.Tests.Profiles;

/// <summary>E-04: one provider account runs several agents, each with its own profile, offerings and approval.</summary>
public class MultiAgentTests(InternalFactory f) : IClassFixture<InternalFactory>, IAsyncLifetime
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    const string Me = "/api/v1/me/practitioner";

    public async Task InitializeAsync() => await WaitForSeed(f);
    public Task DisposeAsync() => Task.CompletedTask;

    static string Account() => "demo-provider-" + Guid.NewGuid().ToString("n");

    static async Task<AgentPrivate> Create(HttpClient c, string name)
    {
        var r = await c.PostAsJsonAsync(Me, new CreateAgentRequest(name), J);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<AgentPrivate>(J))!;
    }

    static async Task<ApiProblem> Problem(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!;

    static async Task<List<AgentSummary>> Agents(HttpClient c)
    {
        var r = await c.GetAsync("/api/v1/me/practitioners");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var doc = await r.Content.ReadFromJsonAsync<JsonElement>(J);
        return doc.GetProperty("items").Deserialize<List<AgentSummary>>(J)!;
    }

    [Fact]
    public async Task One_account_creates_a_second_agent_with_its_own_slug_and_the_list_shows_both_oldest_first()
    {
        var c = f.As(Account());
        var a = await Create(c, "Demo Agent Alfa " + Guid.NewGuid().ToString("n")[..6]);
        f.Clock.Advance(TimeSpan.FromSeconds(1)); // the host's clock is moved by hand: the second agent is created later
        var b = await Create(c, "Demo Agent Beta " + Guid.NewGuid().ToString("n")[..6]);

        Assert.NotEqual(a.Slug, b.Slug);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal([a.Id, b.Id], (await Agents(c)).Select(x => x.Id));
    }

    [Fact]
    public async Task Another_account_sees_none_of_them()
    {
        var owner = f.As(Account());
        await Create(owner, "Demo Agent Gamma");
        Assert.Empty(await Agents(f.As(Account())));
    }

    [Fact]
    public async Task Creating_more_agents_than_the_default_limit_of_ten_is_422_too_many_agents_and_stores_nothing()
    {
        var c = f.As(Account());
        for (var i = 0; i < 10; i++) await Create(c, $"Demo Limit {i} {Guid.NewGuid().ToString("n")[..8]}");
        var r = await c.PostAsJsonAsync(Me, new CreateAgentRequest("Demo Eleventh Agent"), J);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("too_many_agents", (await Problem(r)).Code);
        Assert.Equal(10, (await Agents(c)).Count);
        // another account is not affected by this one's count
        Assert.Equal(HttpStatusCode.Created, (await f.As(Account()).PostAsJsonAsync(Me, new CreateAgentRequest("Demo Other Account"), J)).StatusCode);
    }

    [Fact]
    public async Task With_two_agents_a_call_without_agent_is_422_and_with_agent_it_reaches_only_that_one()
    {
        var c = f.As(Account());
        var a = await Create(c, "Demo Agent Delta " + Guid.NewGuid().ToString("n")[..6]);
        var b = await Create(c, "Demo Agent Epsilon " + Guid.NewGuid().ToString("n")[..6]);

        var bare = await c.GetAsync(Me);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bare.StatusCode);
        Assert.Contains("agent", (await Problem(bare)).Errors!.Keys);

        var put = await c.PutAsJsonAsync($"{Me}?agent={b.Slug}", new UpdateAgentRequest("Demo Epsilon Renamed", "Senior reviewer of agents", ""), J);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var gotB = (await c.GetFromJsonAsync<AgentPrivate>($"{Me}?agent={b.Slug}", J))!;
        var gotA = (await c.GetFromJsonAsync<AgentPrivate>($"{Me}?agent={a.Slug}", J))!;
        Assert.Equal("Demo Epsilon Renamed", gotB.DisplayName);
        Assert.Equal(a.DisplayName, gotA.DisplayName);
    }

    [Fact]
    public async Task An_account_with_one_agent_needs_no_agent_parameter()
    {
        var c = f.As(Account());
        var a = await Create(c, "Demo Solo " + Guid.NewGuid().ToString("n")[..6]);
        Assert.Equal(a.Id, (await c.GetFromJsonAsync<AgentPrivate>(Me, J))!.Id);
    }

    [Fact]
    public async Task Another_accounts_agent_cannot_be_read_edited_or_submitted_and_is_left_unchanged()
    {
        var owner = f.As(Account());
        var mine = await Create(owner, "Demo Owned " + Guid.NewGuid().ToString("n")[..6]);
        var intruder = f.As(Account());
        await Create(intruder, "Demo Intruder " + Guid.NewGuid().ToString("n")[..6]);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"{Me}?agent={mine.Slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PutAsJsonAsync($"{Me}?agent={mine.Slug}", new UpdateAgentRequest("Demo Hijacked", "Senior reviewer of agents", ""), J)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"{Me}/submit?agent={mine.Slug}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsJsonAsync($"{Me}/offerings?agent={mine.Slug}", Valid(), J)).StatusCode);
        Assert.Equal(mine.DisplayName, (await owner.GetFromJsonAsync<AgentPrivate>($"{Me}?agent={mine.Slug}", J))!.DisplayName);
        Assert.Empty(await List(owner, mine.Slug));
    }

    static async Task<List<PackagePrivate>> List(HttpClient c, string slug)
    {
        var doc = await c.GetFromJsonAsync<JsonElement>($"{Me}/offerings?agent={slug}", J);
        return doc.GetProperty("items").Deserialize<List<PackagePrivate>>(J)!;
    }

    [Fact]
    public async Task Offerings_belong_to_one_agent_and_the_active_limit_is_per_agent()
    {
        var c = f.As(Account());
        var a = await Create(c, "Demo Offer A " + Guid.NewGuid().ToString("n")[..6]);
        var b = await Create(c, "Demo Offer B " + Guid.NewGuid().ToString("n")[..6]);

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"{Me}/offerings?agent={a.Slug}", Valid(), J)).StatusCode);
        var sixth = await c.PostAsJsonAsync($"{Me}/offerings?agent={a.Slug}", Valid(), J);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sixth.StatusCode);
        Assert.Equal("too_many_offerings", (await Problem(sixth)).Code);

        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"{Me}/offerings?agent={b.Slug}", Valid(), J)).StatusCode);
        Assert.Equal(5, (await List(c, a.Slug)).Count);
        Assert.Single(await List(c, b.Slug));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.PostAsJsonAsync($"{Me}/offerings", Valid(), J)).StatusCode); // two agents: the parameter is required
    }

    [Fact]
    public async Task Approving_one_agent_leaves_the_other_submitted_and_suspending_one_leaves_the_other_approved()
    {
        var sub = Account();
        var one = await f.Seed(AgentStatus.Submitted, userId: sub);
        var two = await f.Seed(AgentStatus.Submitted, userId: sub);
        var admin = f.As("demo-admin-" + Guid.NewGuid().ToString("n"), "Admin");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/practitioners/{one.Id}/approve", null)).StatusCode);
        Assert.Equal(AgentStatus.Approved, (await f.Load(one.Id)).Status);
        Assert.Equal(AgentStatus.Submitted, (await f.Load(two.Id)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/practitioners/{two.Id}/approve", null)).StatusCode);
        f.Access.Calls.Clear();
        var suspend = await admin.PostAsJsonAsync($"/api/v1/admin/practitioners/{one.Id}/suspend", new { reason = "Violates the rules of the marketplace" }, J);
        Assert.Equal(HttpStatusCode.NoContent, suspend.StatusCode);

        Assert.Equal(AgentStatus.Suspended, (await f.Load(one.Id)).Status);
        Assert.Equal(AgentStatus.Approved, (await f.Load(two.Id)).Status);
        Assert.Equal([one.Id], f.Access.Calls); // only the suspended agent's bookings were cancelled
    }

    [Fact]
    public async Task Two_agents_of_one_account_may_share_a_github_profile_but_another_account_may_not()
    {
        var sub = Account();
        var login = "demo-shared-" + Guid.NewGuid().ToString("n")[..8];
        await f.Seed(AgentStatus.Draft, p => { p.GithubLinked = true; p.GithubLogin = login; }, userId: sub);
        var c = f.As(sub);
        var second = await Create(c, "Demo Second Github " + Guid.NewGuid().ToString("n")[..6]);
        var third = await Create(c, "Demo Third Github " + Guid.NewGuid().ToString("n")[..6]);

        async Task<HttpStatusCode> Link(HttpClient client, string slug)
        {
            var ch = await (await client.PostAsJsonAsync($"{Me}/github-link?agent={slug}", new GithubLinkRequest(login), J)).Content.ReadFromJsonAsync<GithubLinkChallenge>(J);
            f.Github.Exists = true; f.Github.Bio = ch!.Code;
            return (await client.PostAsync($"{Me}/github-link/verify?agent={slug}", null)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.NoContent, await Link(c, second.Slug));

        var other = f.As(Account());
        var foreign = await Create(other, "Demo Foreign Github " + Guid.NewGuid().ToString("n")[..6]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Link(other, foreign.Slug));
        Assert.False((await f.Load(foreign.Id)).GithubLinked);
        Assert.False((await f.Load(third.Id)).GithubLinked); // the third is untouched
    }

    [Fact]
    public async Task Me_lists_the_agents_and_keeps_the_first_as_practitionerId()
    {
        var c = f.As(Account());
        var a = await Create(c, "Demo Me A " + Guid.NewGuid().ToString("n")[..6]);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var b = await Create(c, "Demo Me B " + Guid.NewGuid().ToString("n")[..6]);
        var me = (await c.GetFromJsonAsync<MeDto>("/api/v1/me", J))!;
        Assert.Equal(a.Id, me.AgentId);
        Assert.Equal(AgentStatus.Draft, me.AgentStatus);
        Assert.Equal([a.Id, b.Id], me.Agents!.Select(x => x.Id));
        Assert.Null((await f.As(Account()).GetFromJsonAsync<MeDto>("/api/v1/me", J))!.AgentId);
    }

    [Fact]
    public async Task The_internal_lookups_answer_for_an_account_with_two_agents()
    {
        var sub = Account();
        var one = await f.Seed(AgentStatus.Approved, p => p.ContactEmail = "demo-old@example.test", userId: sub);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var two = await f.Seed(AgentStatus.Draft, p => p.ContactEmail = "demo-new@example.test", userId: sub);
        var i = f.Internal();

        var first = await i.GetFromJsonAsync<JsonElement>($"/internal/v1/practitioners/by-user/{sub}", J);
        Assert.Equal(one.Id, first.GetProperty("practitionerId").GetGuid()); // the oldest: the answer from before E-04
        var all = await i.GetFromJsonAsync<JsonElement>($"/internal/v1/practitioners/by-user/{sub}/agents", J);
        Assert.Equal([one.Id, two.Id], all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("practitionerId").GetGuid()));
        Assert.Equal([one.Slug, two.Slug], all.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await i.GetAsync($"/internal/v1/practitioners/{sub}/contact")).StatusCode);
        Assert.Empty((await i.GetFromJsonAsync<JsonElement>("/internal/v1/practitioners/by-user/demo-nobody/agents", J)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await i.GetAsync("/internal/v1/practitioners/by-user/demo-nobody")).StatusCode);
    }

    [Fact]
    public async Task Erasing_the_account_anonymises_every_agent_after_cancelling_each_ones_bookings()
    {
        var sub = Account();
        var one = await f.Seed(AgentStatus.Approved, userId: sub);
        var two = await f.Seed(AgentStatus.Approved, userId: sub);
        f.Access.Calls.Clear();

        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{sub}")).StatusCode);

        Assert.Equal(new[] { one.Id, two.Id }.Order(), f.Access.Calls.Order());
        foreach (var id in new[] { one.Id, two.Id })
        {
            var row = await f.Load(id);
            Assert.Equal("Usunięty użytkownik", row.DisplayName);
            Assert.NotEqual(sub, row.UserId);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await f.Internal().DeleteAsync($"/internal/v1/subjects/{sub}")).StatusCode); // idempotent
    }
}

/// <summary>The limit is <c>Directory:MaxAgentsPerProvider</c>, here 3.</summary>
public sealed class LimitFactory : InternalFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Directory:MaxAgentsPerProvider", "3");
    }
}

public class MultiAgentConfiguredLimitTests(LimitFactory f) : IClassFixture<LimitFactory>
{
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_limit_is_the_configured_value_and_a_fourth_agent_is_refused_with_the_error_in_the_existing_shape()
    {
        var c = f.As("demo-provider-" + Guid.NewGuid().ToString("n"));
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/v1/me/practitioner", new CreateAgentRequest($"Demo Cfg {i} {Guid.NewGuid().ToString("n")[..8]}"), J)).StatusCode);

        var r = await c.PostAsJsonAsync("/api/v1/me/practitioner", new CreateAgentRequest("Demo Fourth Agent"), J);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var p = (await r.Content.ReadFromJsonAsync<ApiProblem>(J))!;
        Assert.Equal("too_many_agents", p.Code);
        Assert.Equal(422, p.Status);
    }
}
