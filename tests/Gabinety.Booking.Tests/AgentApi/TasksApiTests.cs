using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gabinety.Billing;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Jobs.AgentTasks;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Gabinety.Booking.Tests;

/// <summary>The job restricted to one client: the in-memory database is shared by every host of a run, and an unscoped job would run other tests' queued tasks.</summary>
sealed class ScopedAgentTaskJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config,
    JobMetrics metrics, ILogger<AgentTaskJob> log, string client) : AgentTaskJob(scopes, time, config, metrics, log)
{
    protected override IQueryable<AgentTask> Scope(IQueryable<AgentTask> q) => q.Where(t => t.ClientUserId == client);
    public TimeSpan IntervalOfJob => Interval;
}

static class TaskFx
{
    public static ScopedAgentTaskJob Job(CreateHost h, string client) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock,
            h.Services.GetRequiredService<IConfiguration>(), h.Services.GetRequiredService<JobMetrics>(), h.Services.GetRequiredService<ILogger<AgentTaskJob>>(), client);

    public const string Input = "demo-task: please review the plan of my migration to a modern .NET";

    public static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    public static object Body(string slug, Guid offering, string? input = null) => new { model = slug, offering_id = offering, input = input ?? Input };

    public static Task<AgentTask> Task(CreateHost h, Guid id) => h.Query(db => db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id));
}

public class TasksApiTests
{
    readonly string Me = AgentApiFx.NewUser(), Other = AgentApiFx.NewUser(), Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];
    readonly Guid Offer = Guid.NewGuid();

    Task<Guid> TasksPackage(CreateHost h, string? client = null, long total = 5, long used = 0, BookingStatus status = BookingStatus.Confirmed, TimeSpan? validFor = null, Guid? offering = null) =>
        AgentApiFx.Package(h, client ?? Me, Slug, OfferingKind.Tasks, total, used, status, validFor, offering: offering ?? Offer);

    static async Task<JsonElement> Error(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    static async Task<long> Used(CreateHost h, Guid package) => (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed;

    // ---- POST /v1/tasks

    [Fact]
    public async Task A_task_is_202_queued_takes_one_unit_at_once_and_writes_a_usage_record()
    {
        await using var h = new CreateHost();
        var (key, keyId) = await AgentApiFx.MakeKey(h, Me);
        var package = await TasksPackage(h);

        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer)));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("queued", j.GetProperty("status").GetString());
        var task = await TaskFx.Task(h, j.GetProperty("id").GetGuid());
        Assert.Equal((package, Me, Slug, Offer, AgentTaskStatus.Queued, TaskFx.Input, 0), (task.BookingId, task.ClientUserId, task.PractitionerSlug, task.OfferingId, task.Status, task.InputText, task.Attempts));
        Assert.Equal(1, await Used(h, package));
        var usage = Assert.Single(await h.Query(db => db.UsageRecords.AsNoTracking().Where(u => u.BookingId == package).ToListAsync()));
        Assert.Equal((1L, keyId, OfferingKind.Tasks), (usage.Units, usage.ApiKeyId, usage.Kind));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(20000, true)]
    [InlineData(20001, false)]
    public async Task The_input_is_20_to_20000_characters(int length, bool accepted)
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await TasksPackage(h);
        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer, new string('x', length))));
        Assert.Equal(accepted ? HttpStatusCode.Accepted : HttpStatusCode.BadRequest, r.StatusCode);
        if (!accepted) Assert.Equal("invalid_request_error", (await Error(r)).GetProperty("code").GetString());
        Assert.Equal(accepted ? 1 : 0, await Used(h, package));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"model\":\"x\",\"input\":\"xxxxxxxxxxxxxxxxxxxxxxxxx\"}")]
    [InlineData("{\"model\":\"x\",\"offering_id\":\"nope\",\"input\":\"xxxxxxxxxxxxxxxxxxxxxxxxx\"}")]
    [InlineData("not json")]
    public async Task A_missing_model_or_offering_or_a_bad_body_is_400(string body)
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var r = await AgentApiFx.ApiClient(h, key).PostAsync("/v1/tasks", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task No_package_of_that_offering_is_404_model_not_found_and_a_used_up_one_is_429_insufficient_quota()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var api = AgentApiFx.ApiClient(h, key);
        async Task<HttpResponseMessage> Post(Guid offering) => await api.PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, offering)));

        var none = await Post(Offer);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("model_not_found", (await Error(none)).GetProperty("code").GetString());

        await AgentApiFx.Package(h, Me, Slug, OfferingKind.Chat, offering: Offer); // a Chat package is not a Tasks package
        await TasksPackage(h, Other);                                              // someone else's
        await TasksPackage(h, validFor: TimeSpan.Zero, offering: Guid.NewGuid());  // expired
        Assert.Equal(HttpStatusCode.NotFound, (await Post(Offer)).StatusCode);

        await TasksPackage(h, total: 2, used: 2);
        var full = await Post(Offer);
        Assert.Equal(HttpStatusCode.TooManyRequests, full.StatusCode);
        Assert.Equal("insufficient_quota", (await Error(full)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ten_parallel_orders_for_three_units_give_exactly_three_tasks_and_never_overshoot()
    {
        await using var h = new CreateHost();
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await TasksPackage(h, total: 3);
        var api = AgentApiFx.ApiClient(h, key);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => api.PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer))))));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, await Used(h, package));
        Assert.Equal(3, await h.Query(db => db.AgentTasks.CountAsync(t => t.BookingId == package)));
    }

    // ---- GET /v1/tasks/{id}

    [Fact]
    public async Task The_owner_reads_the_task_through_its_life_and_another_user_gets_404()
    {
        await using var h = new CreateHost();
        h.Settings["Jobs:Enabled"] = "false";
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var (otherKey, _) = await AgentApiFx.MakeKey(h, Other);
        await TasksPackage(h);
        var api = AgentApiFx.ApiClient(h, key);
        var id = JsonDocument.Parse(await (await api.PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer)))).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var queued = JsonDocument.Parse(await (await api.GetAsync($"/v1/tasks/{id}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, queued.GetProperty("id").GetGuid());
        Assert.Equal("queued", queued.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("output").ValueKind);
        Assert.Equal(JsonValueKind.Null, queued.GetProperty("completed_at").ValueKind);
        Assert.EndsWith("Z", queued.GetProperty("created_at").GetString());

        await TaskFx.Job(h, Me).RunOnceAsync(h.Clock.GetUtcNow(), default);
        var done = JsonDocument.Parse(await (await api.GetAsync($"/v1/tasks/{id}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("succeeded", done.GetProperty("status").GetString());
        Assert.StartsWith($"[demo {Slug}] Otrzymano {TaskFx.Input.Length} znaków.", done.GetProperty("output").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("completed_at").ValueKind);

        var theirs = await AgentApiFx.ApiClient(h, otherKey).GetAsync($"/v1/tasks/{id}");
        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/v1/tasks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.CreateClient().GetAsync($"/v1/tasks/{id}")).StatusCode);
    }

    // ---- browser: POST /api/v1/bookings/{id}/tasks, GET /api/v1/me/tasks/{id}, GET /api/v1/me/bookings/{id}/tasks

    [Fact]
    public async Task The_browser_orders_a_task_of_its_own_package_and_reads_it_and_the_list_newest_first()
    {
        await using var h = new CreateHost();
        var package = await TasksPackage(h);
        var me = h.ClientFor(Me);

        var r = await me.PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = TaskFx.Input });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var first = (await r.Body()).GetProperty("id").GetGuid();
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = (await (await me.PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = TaskFx.Input + " 2" })).Body()).GetProperty("id").GetGuid();
        Assert.Equal(2, await Used(h, package));
        var usage = await h.Query(db => db.UsageRecords.AsNoTracking().Where(u => u.BookingId == package).ToListAsync());
        Assert.All(usage, u => Assert.Null(u.ApiKeyId)); // the web panel is not an API key

        var one = await me.GetAsync($"/api/v1/me/tasks/{first}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal("queued", (await one.Body()).GetProperty("status").GetString());

        var list = (await (await me.GetAsync($"/api/v1/me/bookings/{package}/tasks")).Body()).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal([second, first], list);
    }

    [Fact]
    public async Task The_browser_cannot_touch_another_users_package_or_task()
    {
        await using var h = new CreateHost();
        var theirs = await TasksPackage(h, Other);
        var theirTask = (await (await h.ClientFor(Other).PostAsJsonAsync($"/api/v1/bookings/{theirs}/tasks", new { input = TaskFx.Input })).Body()).GetProperty("id").GetGuid();
        var me = h.ClientFor(Me);
        Assert.Equal(HttpStatusCode.NotFound, (await me.PostAsJsonAsync($"/api/v1/bookings/{theirs}/tasks", new { input = TaskFx.Input })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"/api/v1/me/tasks/{theirTask}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync($"/api/v1/me/bookings/{theirs}/tasks")).StatusCode);
        Assert.Equal(1, await Used(h, theirs));
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.CreateClient().PostAsJsonAsync($"/api/v1/bookings/{theirs}/tasks", new { input = TaskFx.Input })).StatusCode);
    }

    [Fact]
    public async Task The_browser_gets_422_for_a_bad_input_and_409_for_a_package_that_cannot_take_a_task()
    {
        await using var h = new CreateHost();
        var me = h.ClientFor(Me);
        var ok = await TasksPackage(h);
        var short_ = await me.PostAsJsonAsync($"/api/v1/bookings/{ok}/tasks", new { input = "too short" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, short_.StatusCode);
        Assert.Equal("validation_failed", (await short_.Body()).GetProperty("code").GetString());

        var chat = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Chat);
        var used = await TasksPackage(h, total: 1, used: 1, offering: Guid.NewGuid());
        var expired = await TasksPackage(h, validFor: TimeSpan.Zero, offering: Guid.NewGuid());
        var pending = await TasksPackage(h, status: BookingStatus.PendingPayment, offering: Guid.NewGuid());
        foreach (var id in new[] { chat, used, expired, pending })
        {
            var r = await me.PostAsJsonAsync($"/api/v1/bookings/{id}/tasks", new { input = TaskFx.Input });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("invalid_transition", (await r.Body()).GetProperty("code").GetString());
        }
        Assert.Equal(0, await Used(h, ok));
    }

    // ---- erasure and demo reset

    [Fact]
    public async Task Erasing_a_user_clears_the_text_of_their_tasks_and_only_theirs()
    {
        await using var h = ErasureApiTests.NewHost();
        var user = ErasureApiTests.NewUser("client");
        var package = await AgentApiFx.Package(h, user, Slug, OfferingKind.Tasks, 5, offering: Offer);
        var other = await AgentApiFx.Package(h, Other, Slug, OfferingKind.Tasks, 5, offering: Offer);
        var mine = (await (await h.ClientFor(user).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = TaskFx.Input })).Body()).GetProperty("id").GetGuid();
        var theirs = (await (await h.ClientFor(Other).PostAsJsonAsync($"/api/v1/bookings/{other}/tasks", new { input = TaskFx.Input })).Body()).GetProperty("id").GetGuid();
        await h.Seed(db => { var t = db.AgentTasks.Single(x => x.Id == mine); t.Status = AgentTaskStatus.Succeeded; t.OutputText = "demo-answer"; });

        Assert.Equal(HttpStatusCode.NoContent, (await ErasureApiTests.Erase(h, user)).StatusCode);
        var erased = await TaskFx.Task(h, mine);
        Assert.Equal(("[usunięto]", null, ErasureApiTests.Token(user)), (erased.InputText, erased.OutputText, erased.ClientUserId));
        var kept = await TaskFx.Task(h, theirs);
        Assert.Equal((TaskFx.Input, Other), (kept.InputText, kept.ClientUserId));
    }

    [Fact]
    public async Task The_demo_reset_removes_the_tasks_of_demo_users_and_their_usage_records()
    {
        var dbName = "tasksreset-" + Guid.NewGuid().ToString("N");
        await using var h = ClockApiTests.FakeHost(x =>
        {
            var inner = x.Configure;
            x.Configure = s =>
            {
                inner?.Invoke(s);
                s.RemoveAll<DbContextOptions<BookingDbContext>>();
                s.RemoveAll(typeof(IDbContextOptionsConfiguration<BookingDbContext>));
                s.RemoveAll<BookingDbContext>();
                s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(dbName));
            };
        });
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 5, offering: Offer);
        var task = (await (await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = TaskFx.Input })).Body()).GetProperty("id").GetGuid();

        var r = await ClockApiTests.PostRaw(h, "/internal/v1/demo/reset", "{}", ClockApiTests.InternalKey);
        var j = await r.Body();
        Assert.Equal(1, j.GetProperty("agentTasks").GetInt32());
        Assert.Equal(1, j.GetProperty("usageRecords").GetInt32());
        Assert.False(await h.Query(db => db.AgentTasks.AnyAsync(t => t.Id == task)));
    }

    // ---- the job's own interval

    [Fact]
    public async Task The_job_runs_every_5_seconds_unless_configured()
    {
        await using var h = new CreateHost();
        Assert.Equal(TimeSpan.FromSeconds(5), TaskFx.Job(h, Me).IntervalOfJob);
        await using var h2 = new CreateHost();
        h2.Settings["Jobs:AgentTasks:IntervalSeconds"] = "2";
        Assert.Equal(TimeSpan.FromSeconds(2), TaskFx.Job(h2, Me).IntervalOfJob);
    }
}
