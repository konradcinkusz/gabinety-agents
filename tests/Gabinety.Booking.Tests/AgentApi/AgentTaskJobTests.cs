using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gabinety.Billing;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Tests;

public class AgentTaskJobTests
{
    const string SecretInput = "demo-task: TRESC-ZADANIA-NIE-DO-LOGOW please do the migration plan";
    const string SecretAnswer = "ODPOWIEDZ-AGENTA-NIE-DO-LOGOW";
    readonly string Me = AgentApiFx.NewUser(), Slug = "demo-agent-" + Guid.NewGuid().ToString("N")[..8];
    readonly Guid Offer = Guid.NewGuid();

    static CreateHost Host(SpyUpstream? spy = null)
    {
        var h = new CreateHost();
        h.Settings["Jobs:Enabled"] = "false";
        if (spy is not null) h.Configure = s => { s.RemoveAll<IAgentUpstream>(); s.AddSingleton<IAgentUpstream>(spy); };
        return h;
    }

    async Task<(Guid Package, Guid Task)> Order(CreateHost h, string input = SecretInput, long total = 5)
    {
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, total, offering: Offer);
        var id = (await (await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input })).Body()).GetProperty("id").GetGuid();
        return (package, id);
    }

    static Task Run(CreateHost h, string client) => TaskFx.Job(h, client).RunOnceAsync(h.Clock.GetUtcNow(), default);

    static string Answer(string content, long tokens = 30) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content } }), ["usage"] = new JsonObject { ["total_tokens"] = tokens } }.ToJsonString();

    [Fact]
    public async Task A_queued_task_is_sent_to_the_platform_as_one_user_message_with_the_metadata_and_succeeds()
    {
        var spy = new SpyUpstream { Answer = () => new UpstreamResult(200, Answer("hello"), 30) };
        await using var h = Host(spy);
        var (package, id) = await Order(h);
        await Run(h, Me);

        var (slug, body) = Assert.Single(spy.Calls);
        Assert.Equal(Slug, slug);
        Assert.Equal(Slug, (string?)body["model"]);
        Assert.False((bool)body["stream"]!);
        var message = Assert.Single(body["messages"]!.AsArray());
        Assert.Equal(("user", SecretInput), ((string?)message!["role"], (string?)message["content"]));
        Assert.Equal(Offer.ToString(), (string?)body["metadata"]!["gabinety_offering_id"]);
        Assert.Equal(id.ToString(), (string?)body["metadata"]!["gabinety_task_id"]);

        var task = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Succeeded, "hello", 1), (task.Status, task.OutputText, task.Attempts));
        Assert.Equal(h.Clock.GetUtcNow(), task.CompletedAt);
        Assert.Equal(1, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed); // the unit stays taken
    }

    [Fact]
    public async Task With_the_fake_platform_the_output_is_not_empty()
    {
        await using var h = Host();
        var (_, id) = await Order(h);
        await Run(h, Me);
        Assert.False(string.IsNullOrWhiteSpace((await TaskFx.Task(h, id)).OutputText));
    }

    [Fact]
    public async Task Three_failures_make_the_task_failed_and_the_unit_goes_back()
    {
        var spy = new SpyUpstream { Throw = new UpstreamException(503) };
        await using var h = Host(spy);
        var (package, id) = await Order(h);

        await Run(h, Me);
        var t1 = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Queued, 1, null), (t1.Status, t1.Attempts, t1.StartedAt));
        await Run(h, Me);
        Assert.Equal((AgentTaskStatus.Queued, 2), ((await TaskFx.Task(h, id)) is var t2 ? (t2.Status, t2.Attempts) : default));
        Assert.Equal(1, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed); // not yet given back

        await Run(h, Me);
        var failed = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Failed, 3, null), (failed.Status, failed.Attempts, failed.OutputText));
        Assert.NotNull(failed.CompletedAt);
        Assert.Equal(3, spy.Calls.Count);
        Assert.Equal(0, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed);
        var units = (await h.Query(db => db.UsageRecords.AsNoTracking().Where(u => u.BookingId == package).Select(u => u.Units).ToListAsync())).Order().ToList();
        Assert.Equal(new List<long> { -1, 1 }, units);

        await Run(h, Me); // a failed task is not picked up again
        Assert.Equal(3, spy.Calls.Count);

        var view = JsonNode.Parse(await (await h.ClientFor(Me).GetAsync($"/api/v1/me/tasks/{id}")).Content.ReadAsStringAsync())!;
        Assert.Equal("failed", (string?)view["status"]);
        Assert.Null(view["output"]);
    }

    [Theory]
    [InlineData(500, "platform text")]
    [InlineData(400, "platform text")]
    [InlineData(200, "not json")]
    [InlineData(200, "{\"choices\":[]}")]
    [InlineData(200, "{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [InlineData(200, "{\"choices\":[{\"message\":{\"content\":\"  \"}}]}")]
    public async Task An_error_answer_or_an_answer_without_text_counts_as_a_failure(int status, string json)
    {
        var spy = new SpyUpstream { Answer = () => new UpstreamResult(status, json, 0) };
        await using var h = Host(spy);
        var (_, id) = await Order(h);
        await Run(h, Me);
        var t = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Queued, 1, null), (t.Status, t.Attempts, t.OutputText));
    }

    [Fact]
    public async Task A_retry_that_succeeds_ends_the_task_succeeded_without_a_refund()
    {
        var calls = 0;
        var spy = new SpyUpstream { Answer = () => ++calls == 1 ? new UpstreamResult(503, "", 0) : new UpstreamResult(200, Answer("second time"), 10) };
        await using var h = Host(spy);
        var (package, id) = await Order(h);
        await Run(h, Me);
        await Run(h, Me);
        var t = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Succeeded, "second time", 2), (t.Status, t.OutputText, t.Attempts));
        Assert.Equal(1, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed);
    }

    [Fact]
    public async Task At_most_5_tasks_run_per_cycle_oldest_first()
    {
        var spy = new SpyUpstream();
        await using var h = Host(spy);
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 20, offering: Offer);
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            ids.Add((await (await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = SecretInput + i })).Body()).GetProperty("id").GetGuid());
            h.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        await Run(h, Me);
        Assert.Equal(5, spy.Calls.Count);
        var states = await h.Query(db => db.AgentTasks.AsNoTracking().Where(t => t.BookingId == package).OrderBy(t => t.CreatedAt).Select(t => t.Status).ToListAsync());
        Assert.Equal([.. Enumerable.Repeat(AgentTaskStatus.Succeeded, 5), AgentTaskStatus.Queued, AgentTaskStatus.Queued], states);
        await Run(h, Me);
        Assert.Equal(7, spy.Calls.Count);
    }

    [Fact]
    public async Task A_task_that_ran_past_the_timeout_is_treated_as_a_failure_and_a_recent_one_is_left_alone()
    {
        var spy = new SpyUpstream();
        await using var h = Host(spy);
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 5, used: 2, offering: Offer);
        var now = h.Clock.GetUtcNow();
        Guid stuck = Guid.NewGuid(), recent = Guid.NewGuid();
        await h.Seed(db =>
        {
            AgentTask Make(Guid id, DateTimeOffset started, int attempts) => new()
            {
                Id = id, BookingId = package, ClientUserId = Me, PractitionerSlug = Slug, OfferingId = Offer, Status = AgentTaskStatus.Running, InputText = SecretInput,
                Attempts = attempts, CreatedAt = started, StartedAt = started,
            };
            db.AgentTasks.Add(Make(stuck, now.AddMinutes(-31), 3));  // out of attempts: Failed
            db.AgentTasks.Add(Make(recent, now.AddMinutes(-29), 1));
        });
        await Run(h, Me);
        var s = await TaskFx.Task(h, stuck);
        Assert.Equal(AgentTaskStatus.Failed, s.Status);
        Assert.Equal(AgentTaskStatus.Running, (await TaskFx.Task(h, recent)).Status);
        Assert.Equal(1, (await h.Query(db => db.Bookings.AsNoTracking().SingleAsync(b => b.Id == package))).UnitsUsed); // one unit back
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task A_stuck_task_with_attempts_left_runs_again()
    {
        var spy = new SpyUpstream();
        await using var h = Host(spy);
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 5, used: 1, offering: Offer);
        var now = h.Clock.GetUtcNow();
        var id = Guid.NewGuid();
        await h.Seed(db => db.AgentTasks.Add(new AgentTask
        {
            Id = id, BookingId = package, ClientUserId = Me, PractitionerSlug = Slug, OfferingId = Offer, Status = AgentTaskStatus.Running, InputText = SecretInput,
            Attempts = 1, CreatedAt = now.AddHours(-1), StartedAt = now.AddHours(-1),
        }));
        await Run(h, Me);
        var t = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Succeeded, 2), (t.Status, t.Attempts));
        Assert.Single(spy.Calls);
    }

    [Fact]
    public async Task The_timeout_setting_is_read()
    {
        var spy = new SpyUpstream();
        await using var h = Host(spy);
        h.Settings["Agents:TaskTimeoutMinutes"] = "5";
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 5, used: 1, offering: Offer);
        var now = h.Clock.GetUtcNow();
        var id = Guid.NewGuid();
        await h.Seed(db => db.AgentTasks.Add(new AgentTask
        {
            Id = id, BookingId = package, ClientUserId = Me, PractitionerSlug = Slug, OfferingId = Offer, Status = AgentTaskStatus.Running, InputText = SecretInput,
            Attempts = 3, CreatedAt = now.AddMinutes(-6), StartedAt = now.AddMinutes(-6),
        }));
        await Run(h, Me);
        Assert.Equal(AgentTaskStatus.Failed, (await TaskFx.Task(h, id)).Status);
    }

    [Fact]
    public async Task A_deployed_host_without_a_platform_starts_and_the_job_claims_nothing_until_one_is_configured()
    {
        await using var h = new CreateHost().AsDeployed(); // Agents:Provider is not set and the fake platform is refused there (AgentsFeature)
        h.Settings["Jobs:Enabled"] = "false";
        await Run(h, Me); // no task: nothing needs the platform, so the job stays quiet
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 5, used: 1, offering: Offer);
        var id = Guid.NewGuid();
        await h.Seed(db => db.AgentTasks.Add(new AgentTask
        {
            Id = id, BookingId = package, ClientUserId = Me, PractitionerSlug = Slug, OfferingId = Offer, Status = AgentTaskStatus.Queued, InputText = SecretInput, CreatedAt = h.Clock.GetUtcNow(),
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(h, Me));
        var t = await TaskFx.Task(h, id);
        Assert.Equal((AgentTaskStatus.Queued, 0), (t.Status, t.Attempts)); // nothing was claimed, so nothing is stuck Running
    }

    // ---- rule 6: the text of a task and of its answer is never logged

    [Fact]
    public async Task Neither_the_task_nor_the_answer_nor_an_exception_text_reaches_the_log()
    {
        var spy = new SpyUpstream { Answer = () => new UpstreamResult(200, Answer(SecretAnswer), 30) };
        await using var h = Host(spy);
        var (key, _) = await AgentApiFx.MakeKey(h, Me);
        var package = await AgentApiFx.Package(h, Me, Slug, OfferingKind.Tasks, 10, offering: Offer);

        // by key and by browser, succeeding
        await AgentApiFx.ApiClient(h, key).PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer, SecretInput)));
        await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = SecretInput });
        await Run(h, Me);
        // failing with an exception whose message carries the task, until Failed
        spy.Throw = new InvalidOperationException("boom " + SecretInput + SecretAnswer);
        await AgentApiFx.ApiClient(h, key).PostAsync("/v1/tasks", TaskFx.Json(TaskFx.Body(Slug, Offer, SecretInput)));
        for (var i = 0; i < 3; i++) await Run(h, Me);
        // the answers that read the text back
        await h.ClientFor(Me).GetAsync($"/api/v1/me/bookings/{package}/tasks");
        // and the answer text stays out of the failing path too: a platform body with the secret that is not an answer
        spy.Throw = null;
        spy.Answer = () => new UpstreamResult(500, SecretAnswer + SecretInput, 0);
        await h.ClientFor(Me).PostAsJsonAsync($"/api/v1/bookings/{package}/tasks", new { input = SecretInput });
        for (var i = 0; i < 3; i++) await Run(h, Me);

        Assert.Contains(h.Logs.Lines, l => l.Contains("queued for package")); // the logger did run
        Assert.DoesNotContain(h.Logs.Lines, l => l.Contains("TRESC-ZADANIA") || l.Contains("ODPOWIEDZ-AGENTA") || l.Contains("boom") || l.Contains(key[4..]));
    }
}
