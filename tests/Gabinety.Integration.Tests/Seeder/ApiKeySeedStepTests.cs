using Gabinety.DemoSeeder;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>D-01: the API key of demo-client. The key is shown once by the service; the seeder logs the prefix only.</summary>
public class ApiKeySeedStepTests
{
    [Fact]
    public async Task The_key_is_created_for_demo_client_the_log_names_only_its_prefix_and_a_second_run_creates_nothing()
    {
        await using var w = new BookingWorld();
        await w.SeedDirectoryAsync();
        var step = new ApiKeySeedStep();

        await step.RunAsync(w.Context, CancellationToken.None);

        var keys = await w.Booking.Db(db => db.ApiKeys.AsNoTracking().ToListAsync());
        var key = Assert.Single(keys);
        Assert.Equal("demo-seed-key", key.Name);
        Assert.Equal(8, key.Prefix.Length);
        Assert.Matches("^[0-9a-f]{64}$", key.Hash); // only the hash is stored
        Assert.Contains($"prefix {key.Prefix}", w.Log.Text);
        Assert.DoesNotMatch(@"gab_[A-Za-z0-9]{10,}", w.Log.Text); // never the key (the prefix is 8 characters)
        Assert.DoesNotContain(key.Hash, w.Log.Text);

        var again = w.NewContext(); // a real second run rebuilds the context from the systems first
        await new DirectorySeedStep().RunAsync(again, CancellationToken.None);
        w.Calls.Requests.Clear();
        await step.RunAsync(again, CancellationToken.None);

        Assert.Single(await w.Booking.Db(db => db.ApiKeys.AsNoTracking().ToListAsync()));
        Assert.DoesNotContain(w.Calls.Requests, r => r.Method == HttpMethod.Post);
        Assert.Contains($"found, prefix {key.Prefix}", w.Log.Text);
    }

    [Fact]
    public async Task Without_a_Booking_endpoint_the_step_says_so_and_does_nothing()
    {
        await using var w = new BookingWorld();
        var context = new SeedContext(new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword), w.Http, w.Log, TimeProvider.System);

        await new ApiKeySeedStep().RunAsync(context, CancellationToken.None);

        Assert.Contains("apikey: skipped", w.Log.Text);
        Assert.Empty(w.Calls.Requests);
    }

    [Fact]
    public void The_step_runs_after_Booking_and_before_Reputation()
    {
        var names = SeedSteps.All.Select(s => s.Name).ToList();
        Assert.True(names.IndexOf("booking") < names.IndexOf("apikey") && names.IndexOf("apikey") < names.IndexOf("reputation"));
    }
}
