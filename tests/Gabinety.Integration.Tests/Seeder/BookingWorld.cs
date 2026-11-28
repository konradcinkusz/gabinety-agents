extern alias BookingApp;
extern alias DirectoryApp;

using System.Net.Http.Json;
using System.Text.Json;
using BookingApp::Gabinety.Booking.Clients;
using BookingApp::Gabinety.Booking.Features.Internal.TestClock;
using BookingApp::Gabinety.Booking.Jobs.AgentTasks;
using BookingApp::Gabinety.Booking.Jobs.AutoComplete;
using BookingApp::Gabinety.Booking.Persistence;
using Gabinety.DemoSeeder;
using Gabinety.Integration.Tests.AuthContract;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AccessEntity = BookingApp::Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>The Booking service in process, in the fake profile (GABINETY_FAKE=1, so the clock and reset endpoints exist), on its own InMemory database and a Directory it reaches in process.</summary>
public sealed class BookingHost : WebApplicationFactory<BookingApp::Program>
{
    public const string Key = "demo-booking-internal-key-for-seeder-tests";
    readonly DevIdentityHost _identity;
    readonly DirectoryHost _directory;

    public BookingHost(DevIdentityHost identity, DirectoryHost directory) { _identity = identity; _directory = directory; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Internal:Key", Key);
        builder.UseSetting("Identity:Authority", DirectoryHost.Authority);
        builder.UseSetting("GABINETY_FAKE", "1");
        var dbName = "seeder-booking-" + Guid.NewGuid().ToString("N");
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<AccessDbContext>>();
            s.RemoveAll(typeof(IDbContextOptionsConfiguration<AccessDbContext>));
            s.RemoveAll<AccessDbContext>();
            s.AddDbContext<AccessDbContext>(o => o.UseInMemoryDatabase(dbName));
            s.RemoveAll<IDirectoryClient>();
            s.AddSingleton<IDirectoryClient>(new DirectoryClient(_directory.CreateDefaultClient(), DirectoryHost.Key, retryDelay: TimeSpan.Zero));
            s.PostConfigure<RateLimiterOptions>(o => o.GlobalLimiter = null);
            s.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = _identity.Server.CreateHandler());
        });
    }

    public long ClockOffsetMinutes => Services.GetRequiredService<TestClockOffset>().Minutes;
    public void SetClockOffset(long minutes) => Services.GetRequiredService<TestClockOffset>().Minutes = minutes;
    public DateTimeOffset Now => Services.GetRequiredService<TimeProvider>().GetUtcNow();

    public async Task<T> Db<T>(Func<AccessDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AccessDbContext>());
    }

    public Task<List<AccessEntity>> Accesses() => Db(db => db.Accesses.AsNoTracking().OrderBy(b => b.CreatedAt).ThenBy(b => b.Id).ToListAsync());
}

/// <summary>
/// Drives the service's own background jobs on the Booking clock every few milliseconds, because their real timers tick every 5 s and 60 s: the agent task job (queued tasks get an answer from the
/// fake platform and become Succeeded) and the auto-complete job (a package whose last unit is used becomes Completed). It writes nothing itself, so the state the seeder's tests read is what the real code made.
/// Without it the seeded packages stay as the API left them (tasks Queued, used-up packages Confirmed), which is what the "no job" tests want.
/// </summary>
public sealed class BookingJobDriver : IAsyncDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;

    public BookingJobDriver(BookingHost access)
    {
        _ = access.Services; // build the host here: two threads touching a factory's Services before it exists would build two servers, and two databases
        var tasks = access.Services.GetRequiredService<AgentTaskJob>();
        var complete = access.Services.GetRequiredService<AutoCompleteJob>();
        _loop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await tasks.RunOnceAsync(access.Now, _stop.Token);
                    await complete.CompleteDueAsync(access.Now, _stop.Token);
                    await Task.Delay(20, _stop.Token);
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { return; }
            }
        });
    }

    public async ValueTask DisposeAsync() { _stop.Cancel(); await _loop; }
}

/// <summary>Records every request to Booking and can fail the ones a test names.</summary>
public sealed class BookingCalls : DelegatingHandler
{
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];
    public Func<HttpRequestMessage, bool>? FailWhen { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
        if (FailWhen?.Invoke(request) == true) throw new HttpRequestException("scripted failure");
        return base.SendAsync(request, ct);
    }
}

public sealed class WorldSeedHttp(DevIdentityHost identity, DirectoryHost directory, BookingHost access, BookingCalls bookingCalls) : ISeedHttp
{
    HttpClient? _booking;
    public HttpClient For(string service) => service switch
    {
        SeedService.Identity => identity.CreateClient(),
        SeedService.Directory => directory.CreateClient(),
        SeedService.Access => _booking ??= access.CreateDefaultClient(bookingCalls),
        _ => throw new InvalidOperationException(service),
    };
}

/// <summary>Identity, Directory and Booking in process. <see cref="SeedDirectoryAsync"/> runs the real Directory step; the Booking step is run by the test.</summary>
public sealed class BookingWorld : IAsyncDisposable
{
    public DevIdentityHost Identity { get; } = new();
    public DirectoryHost Directory { get; }
    public BookingHost Access { get; }
    public BookingCalls Calls { get; } = new();
    public SeedOptions Options { get; }
    public CapturedLog Log { get; } = new();
    public SeedContext Context { get; private set; } = null!;
    public WorldSeedHttp Http { get; }

    public BookingWorld()
    {
        Directory = new DirectoryHost(Identity);
        Access = new BookingHost(Identity, Directory);
        Options = new SeedOptions("http://localhost", DirectoryHost.Key, DirectoryHost.Authority, SeedOptions.DevIdentityPassword)
            { BookingUrl = "http://localhost", BookingKey = BookingHost.Key };
        Http = new WorldSeedHttp(Identity, Directory, Access, Calls);
    }

    public async Task SeedDirectoryAsync()
    {
        _ = Access.Services; // one server, built before anything else touches the factory
        Context = NewContext();
        await new DirectorySeedStep().RunAsync(Context, CancellationToken.None);
    }

    public SeedContext NewContext() => new(Options, Http, Log, TimeProvider.System);

    /// <summary>The Booking step as the tests run it: a short wait for the completion job, a quick poll.</summary>
    public static BookingSeedStep Step(TimeSpan? wait = null) =>
        new() { CompletionWait = wait ?? TimeSpan.FromSeconds(20), CompletionPoll = TimeSpan.FromMilliseconds(25), TaskWait = wait ?? TimeSpan.FromSeconds(20) };

    /// <summary>What a person would read: a client's own bookings through the public API, by the client's demo name.</summary>
    public async Task<List<JsonElement>> BookingsOf(string clientName)
    {
        using var http = Access.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", await TokenAsync(clientName));
        var all = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await http.GetFromJsonAsync<JsonElement>("/api/v1/me/bookings?role=client&limit=24" + (cursor is null ? "" : "&cursor=" + cursor));
            all.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.Clone()));
            cursor = page.TryGetProperty("nextCursor", out var c) ? c.GetString() : null;
        } while (cursor is not null);
        return all;
    }

    public async Task<List<JsonElement>> AllClientBookings()
    {
        var all = new List<JsonElement>();
        foreach (var name in DemoCatalogNames) all.AddRange(await BookingsOf(name));
        return all;
    }

    public static IEnumerable<string> DemoCatalogNames => ["demo-client", "demo-client-2", "demo-client-3", "demo-client-4"];

    public async Task<string> TokenAsync(string userName)
    {
        using var http = Identity.CreateClient();
        using var res = await http.PostAsJsonAsync("api/v1/auth/login", new { email = $"{userName}@demo.example", password = SeedOptions.DevIdentityPassword });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    public static Dictionary<string, int> StatusCounts(IEnumerable<JsonElement> accesses) =>
        accesses.GroupBy(b => b.GetProperty("status").GetString()!).ToDictionary(g => g.Key, g => g.Count());

    public async ValueTask DisposeAsync() { Access.Dispose(); Directory.Dispose(); Identity.Dispose(); await Task.CompletedTask; }
}
