using Gabinety.Booking.Clients;
using Gabinety.Contracts.Directory;

namespace Gabinety.Booking.Tests;

public class PractitionerResolverTests
{
    sealed class Counting(Func<string, DirectoryAgent[]> answer) : IDirectoryClient
    {
        public int Calls;
        public Task<IReadOnlyList<DirectoryAgent>> GetPractitionerAgentsAsync(string userId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<DirectoryAgent>>(answer(userId));
        }
        public Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryOffering?> GetOfferingAsync(Guid offeringId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryPractitionerName?> GetPractitionerNameAsync(Guid practitionerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsAliveAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    static DirectoryAgent Agent(Guid id, string slug = "demo-agent") => new(id, PractitionerStatus.Approved, slug);

    [Fact]
    public async Task A_cached_user_is_asked_again_only_after_60_seconds()
    {
        var clock = new ManualClock();
        var id = Guid.NewGuid();
        var dir = new Counting(_ => [Agent(id)]);
        var resolver = new PractitionerResolver(dir, clock);

        Assert.Equal(id, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(id, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId);
        Assert.Equal(1, dir.Calls);
        clock.Advance(TimeSpan.FromSeconds(2)); // 61 s since the first answer
        Assert.Equal(id, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId);
        Assert.Equal(2, dir.Calls);
        Assert.Equal(id, (await resolver.ResolveAsync("demo-user-2", null, default)).PractitionerId);
        Assert.Equal(3, dir.Calls); // another user is a separate entry
    }

    [Fact]
    public async Task Clear_makes_the_next_resolve_ask_the_directory_again()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = first;
        var dir = new Counting(_ => [Agent(current)]);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Equal(first, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId);

        current = second; // the Directory was reset and reseeded: the same user has a new practitioner id
        Assert.Equal(first, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId); // still inside the 60 s
        resolver.Clear();
        Assert.Equal(second, (await resolver.ResolveAsync("demo-user-1", null, default)).PractitionerId);
    }

    [Fact]
    public async Task A_non_practitioner_resolves_to_nothing_and_is_not_cached()
    {
        var dir = new Counting(_ => []);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Equal(new AgentResolution(null, false), await resolver.ResolveAsync("demo-user-1", null, default));
        Assert.Equal(new AgentResolution(null, false), await resolver.ResolveAsync("demo-user-1", "demo-agent", default));
        Assert.Equal(2, dir.Calls);
    }

    [Fact]
    public async Task With_two_agents_a_slug_picks_one_and_no_slug_is_ambiguous()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var resolver = new PractitionerResolver(new Counting(_ => [Agent(a, "demo-a"), Agent(b, "demo-b")]), new ManualClock());

        Assert.Equal(new AgentResolution(null, true), await resolver.ResolveAsync("demo-user-1", null, default));
        Assert.Equal(new AgentResolution(null, true), await resolver.ResolveAsync("demo-user-1", "  ", default));
        Assert.Equal(b, (await resolver.ResolveAsync("demo-user-1", "demo-b", default)).PractitionerId);
        Assert.Equal(a, (await resolver.ResolveAsync("demo-user-1", " demo-a ", default)).PractitionerId);
    }

    [Fact]
    public async Task A_slug_that_is_not_the_users_resolves_to_nothing_even_with_one_agent()
    {
        var resolver = new PractitionerResolver(new Counting(_ => [Agent(Guid.NewGuid(), "demo-mine")]), new ManualClock());
        Assert.Equal(new AgentResolution(null, false), await resolver.ResolveAsync("demo-user-1", "demo-someone-elses", default));
    }

    [Fact]
    public async Task A_new_agent_created_within_the_minute_is_found_by_asking_again_once()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var known = new List<DirectoryAgent> { Agent(a, "demo-a") };
        var dir = new Counting(_ => [.. known]);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Equal(a, (await resolver.ResolveAsync("demo-user-1", "demo-a", default)).PractitionerId);

        known.Add(Agent(b, "demo-b")); // created after the list was cached
        Assert.Equal(b, (await resolver.ResolveAsync("demo-user-1", "demo-b", default)).PractitionerId);
        Assert.Equal(2, dir.Calls);
        Assert.Equal(new AgentResolution(null, false), await resolver.ResolveAsync("demo-user-1", "demo-c", default));
        Assert.Equal(3, dir.Calls); // an unknown slug costs one more call, not a loop
    }
}
