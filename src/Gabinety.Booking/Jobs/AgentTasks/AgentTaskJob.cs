using System.Text.Json;
using System.Text.Json.Nodes;
using Gabinety.Booking.Agents.Abstractions;
using Gabinety.Booking.Features.AgentApi;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Jobs.AgentTasks;

/// <summary>
/// Runs the queued tasks (docs/agentic/PLAN.md B-05). Every <see cref="DefaultIntervalSeconds"/> seconds (<c>Jobs:AgentTasks:IntervalSeconds</c>) it takes the
/// <see cref="BatchSize"/> oldest <c>Queued</c> tasks, marks them <c>Running</c> and asks the agent platform (<c>stream:false</c>, one <c>user</c> message with the task, <c>metadata</c>
/// with the offering and task ids). The text of <c>choices[0].message.content</c> becomes the output and the task <c>Succeeded</c>. A failure (an error answer, an exception, an answer
/// without text) puts the task back in the queue; after <see cref="MaxAttempts"/> starts it is <c>Failed</c> and its unit goes back to the package. A task that has been
/// <c>Running</c> for <c>Agents:TaskTimeoutMinutes</c> (default 30) is treated as a failure. Only ids, counts and exception types are logged: never the task or the answer (rule 6).
/// </summary>
public class AgentTaskJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<AgentTaskJob> log)
    : PeriodicJob(time, config, metrics, log)
{
    public const string JobName = "agent_tasks";
    public const int DefaultIntervalSeconds = 5, BatchSize = 5, MaxAttempts = 3, DefaultTimeoutMinutes = 30;
    static readonly SemaphoreSlim InMemoryGate = new(1, 1);

    public override string Name => JobName;

    protected override TimeSpan Interval =>
        double.TryParse(Configuration["Jobs:AgentTasks:IntervalSeconds"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0 && s < 86_400
            ? TimeSpan.FromSeconds(s) : TimeSpan.FromSeconds(DefaultIntervalSeconds);

    public override async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await FailStuckAsync(now, ct);
        var claimed = await ClaimAsync(now, ct);
        var outcomes = await Task.WhenAll(claimed.Select(id => RunTaskAsync(id, now, ct)));
        var failed = outcomes.Count(ok => !ok);
        if (claimed.Count > 0) log.LogInformation("AgentTasks ran {Count} task(s), {Failed} without an answer", claimed.Count, failed);
    }

    /// <summary>Narrows what the job looks at. The default is everything; a test sharing a database with other tests restricts it to its own rows.</summary>
    protected virtual IQueryable<AgentTask> Scope(IQueryable<AgentTask> tasks) => tasks;

    async Task FailStuckAsync(DateTimeOffset now, CancellationToken ct)
    {
        var limit = now - TimeSpan.FromMinutes(ReadPositiveInt(Configuration, "Agents:TaskTimeoutMinutes", DefaultTimeoutMinutes));
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var stuck = await Scope(db.AgentTasks).Where(t => t.Status == AgentTaskStatus.Running && t.StartedAt <= limit).Select(t => t.Id).ToListAsync(ct);
        foreach (var id in stuck) await FailAsync(id, now, ct);
        if (stuck.Count > 0) log.LogWarning("AgentTasks gave up on {Count} task(s) that ran past the timeout", stuck.Count);
    }

    /// <summary>Takes the oldest queued tasks: each claim is one conditional update, so two instances never run the same task.</summary>
    async Task<List<Guid>> ClaimAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var ids = await Scope(db.AgentTasks.AsNoTracking()).Where(t => t.Status == AgentTaskStatus.Queued).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Id).Take(BatchSize).ToListAsync(ct);
        var claimed = new List<Guid>();
        // The platform is resolved only when there is work, and before anything is claimed: a deployed host without Agents:Provider still starts (AgentsFeature) and its job stays quiet until a task exists.
        if (ids.Count > 0) _ = scope.ServiceProvider.GetRequiredService<IAgentUpstream>();
        foreach (var id in ids)
        {
            bool won;
            if (db.Database.IsRelational())
                won = await db.AgentTasks.Where(t => t.Id == id && t.Status == AgentTaskStatus.Queued)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Running).SetProperty(t => t.StartedAt, now).SetProperty(t => t.Attempts, t => t.Attempts + 1), ct) > 0;
            else
            {
                await InMemoryGate.WaitAsync(ct);
                try
                {
                    var t = await db.AgentTasks.SingleAsync(x => x.Id == id, ct);
                    won = t.Status == AgentTaskStatus.Queued;
                    if (won) { t.Status = AgentTaskStatus.Running; t.StartedAt = now; t.Attempts++; await db.SaveChangesAsync(ct); }
                }
                finally { InMemoryGate.Release(); }
            }
            if (won) claimed.Add(id);
        }
        return claimed;
    }

    /// <summary>One task: true when the platform answered with text.</summary>
    async Task<bool> RunTaskAsync(Guid id, DateTimeOffset started, CancellationToken ct)
    {
        string? output = null;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id, ct);
            var body = new JsonObject
            {
                ["model"] = task.PractitionerSlug, ["stream"] = false,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = task.InputText }),
                ["metadata"] = new JsonObject { ["gabinety_offering_id"] = task.OfferingId.ToString(), ["gabinety_task_id"] = task.Id.ToString() },
            };
            var result = await scope.ServiceProvider.GetRequiredService<IAgentUpstream>().CompleteAsync(task.PractitionerSlug, body, ct);
            if (result.StatusCode is >= 200 and < 300) output = AnswerOf(result.Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("AgentTasks: the platform did not answer task {TaskId}: {ExceptionType}", id, ex.GetType().Name);
        }

        using var finish = scopes.CreateScope();
        var now = started; // one reading of the clock per cycle, like the other jobs
        if (output is null) { await FailAsync(id, now, ct, finish); return false; }
        var fdb = finish.ServiceProvider.GetRequiredService<BookingDbContext>();
        var t = await fdb.AgentTasks.SingleAsync(x => x.Id == id, ct);
        if (t.Status != AgentTaskStatus.Running) return true; // another instance gave up on it meanwhile
        t.Status = AgentTaskStatus.Succeeded;
        t.OutputText = output;
        t.CompletedAt = now;
        await fdb.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Back to the queue, or <c>Failed</c> with the unit refunded once <see cref="MaxAttempts"/> starts were used.</summary>
    async Task FailAsync(Guid id, DateTimeOffset now, CancellationToken ct, IServiceScope? use = null)
    {
        using var own = use is null ? scopes.CreateScope() : null;
        var sp = (use ?? own!).ServiceProvider;
        var db = sp.GetRequiredService<BookingDbContext>();
        var t = await db.AgentTasks.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null || t.Status != AgentTaskStatus.Running) return;
        if (t.Attempts < MaxAttempts)
        {
            t.Status = AgentTaskStatus.Queued;
            t.StartedAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }
        t.Status = AgentTaskStatus.Failed;
        t.CompletedAt = now;
        db.UsageRecords.Add(new UsageRecord { Id = Guid.NewGuid(), BookingId = t.BookingId, ApiKeyId = null, Kind = OfferingKind.Tasks, Units = -1, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await sp.GetRequiredService<EntitlementStore>().RefundUnitsAsync(t.BookingId, 1, ct);
        log.LogWarning("AgentTasks: task {TaskId} failed after {Attempts} attempts, its unit went back to package {BookingId}", t.Id, t.Attempts, t.BookingId);
    }

    /// <summary><c>choices[0].message.content</c> when it is text, else null (also for a body that is not JSON). Nothing of the body is put in an exception or a log.</summary>
    internal static string? AnswerOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
            var c = choices[0];
            if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Registers the task job as a singleton (a test resolves it and calls <see cref="AgentTaskJob.RunOnceAsync"/>) and as the hosted service that runs it. <c>Jobs:Enabled=false</c> stops the loop.</summary>
public sealed class AgentTaskFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AgentTaskJob>();
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AgentTaskJob>());
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
