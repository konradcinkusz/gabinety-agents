using Gabinety.Booking.Clients;
using Gabinety.Contracts.Directory;
using Gabinety.Contracts.Booking;

namespace Gabinety.Booking.Tests;

public class PractitionerResolverTests
{
    sealed class Counting(Func<string, Guid?> answer) : IDirectoryClient
    {
        public int Calls;
        public Task<DirectoryPractitioner?> GetPractitionerByUserAsync(string userId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer(userId) is { } id ? new DirectoryPractitioner(id, PractitionerStatus.Approved) : null);
        }
        public Task<DirectoryOffering?> GetOfferingAsync(Guid offeringId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryPractitionerName?> GetPractitionerNameAsync(Guid practitionerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DirectoryContact?> GetPractitionerContactAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsAliveAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_cached_user_is_asked_again_only_after_60_seconds()
    {
        var clock = new ManualClock();
        var id = Guid.NewGuid();
        var dir = new Counting(_ => id);
        var resolver = new PractitionerResolver(dir, clock);

        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(1, dir.Calls);
        clock.Advance(TimeSpan.FromSeconds(2)); // 61 s since the first answer
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(2, dir.Calls);
        Assert.Equal(id, await resolver.ResolveAsync("demo-user-2", default));
        Assert.Equal(3, dir.Calls); // another user is a separate entry
    }

    [Fact]
    public async Task Clear_makes_the_next_resolve_ask_the_directory_again()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var current = first;
        var dir = new Counting(_ => current);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Equal(first, await resolver.ResolveAsync("demo-user-1", default));

        current = second; // the Directory was reset and reseeded: the same user has a new practitioner id
        Assert.Equal(first, await resolver.ResolveAsync("demo-user-1", default)); // still inside the 60 s
        resolver.Clear();
        Assert.Equal(second, await resolver.ResolveAsync("demo-user-1", default));
    }

    [Fact]
    public async Task A_non_practitioner_resolves_to_null_and_is_not_cached()
    {
        var dir = new Counting(_ => null);
        var resolver = new PractitionerResolver(dir, new ManualClock());
        Assert.Null(await resolver.ResolveAsync("demo-user-1", default));
        Assert.Null(await resolver.ResolveAsync("demo-user-1", default));
        Assert.Equal(2, dir.Calls);
    }
}
