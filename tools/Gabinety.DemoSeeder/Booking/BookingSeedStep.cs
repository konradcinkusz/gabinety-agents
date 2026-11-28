namespace Gabinety.DemoSeeder;

/// <summary>
/// The Booking data (T-08.01b, rewritten for packages in D-01): eleven purchases of the agents' packages in the states the journeys need, all through the public and user APIs
/// of Booking (create, simulated payment, order a task, cancel, claim, admin resolve). Nothing is written to the database and the test clock is not touched: a package is valid
/// for 30 days from the purchase, its tasks are ordered through <c>POST /api/v1/bookings/{id}/tasks</c> (so <c>UnitsUsed</c> is real and Reputation may review it), and using
/// the last unit of a single-task package lets the service's auto-complete job complete it. Idempotent: a booking is found again by the demo key in its brief and only the steps it
/// has not yet passed are taken; the tasks already ordered are counted from <c>UnitsUsed</c>.
/// </summary>
public sealed class BookingSeedStep : ISeedStep
{
    public string Name => "booking";
    public int Order => 200;

    /// <summary>How long to wait for the service's auto-complete job to complete the packages whose last unit is used (the job runs every 60 s).</summary>
    public TimeSpan CompletionWait { get; init; } = TimeSpan.FromSeconds(75);
    public TimeSpan CompletionPoll { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>How long to wait for the agent platform's answers to the tasks just ordered (the task job runs every 5 s). Not an error when it is longer: the tasks stay queued and the rest of the data is valid.</summary>
    public TimeSpan TaskWait { get; init; } = TimeSpan.FromSeconds(30);

    sealed record Work(DemoBooking Plan, DemoUser Client, SeededPractitioner Seeded, BookingInfo Info);

    public async Task<int> ResetAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.BookingUrl.Length == 0) return -1;
        if (context.Options.BookingKey.Length == 0) throw new SeedException("booking: Internal:Keys:Booking is not configured");
        return await new BookingApi(context.Http.For(SeedService.Access), context.Options, context.Time).ResetAsync(ct);
    }

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.BookingUrl.Length == 0) { context.Log.Info("booking: skipped (Backend:Booking is not configured)"); return; }
        if (context.Options.BookingKey.Length == 0) throw new SeedException("booking: Internal:Keys:Booking is not configured");
        var api = new BookingApi(context.Http.For(SeedService.Access), context.Options, context.Time);
        var approved = context.Agents.Values.Where(p => p.Status == "Approved" && p.Packages.Count > 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (approved.Count == 0) throw new SeedException("booking: no Approved demo agent with a package (run the directory step first)");
        var clients = context.Clients.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Value).ToList();
        if (clients.Count == 0) throw new SeedException("booking: no demo clients (run the directory step first)");
        var admin = await context.Identity.SignInAsync("demo-admin", ct);

        var changes = 0;
        var known = await KnownAsync(api, clients, ct);
        var works = new List<Work>();
        foreach (var plan in BookingPlan.All)
        {
            var (work, created) = await BuildAsync(context, api, plan, approved, clients, known, ct);
            if (created) changes++;
            var (advanced, n) = await AdvanceAsync(api, admin, work, context, ct);
            changes += n;
            works.Add(advanced);
        }

        await WaitForCompletedAsync(context, api, works.Where(w => w.Plan.Target == BookingTarget.Completed).ToList(), ct);
        await WaitForTasksAsync(context, api, works.Where(w => w.Plan.Tasks > 0).ToList(), ct);
        context.Log.Info($"booking: {BookingPlan.All.Length} demo bookings {(changes == 0 ? "unchanged" : $"changes={changes}")}");
    }

    static async Task<Dictionary<string, BookingInfo>> KnownAsync(BookingApi api, List<DemoUser> clients, CancellationToken ct)
    {
        var known = new Dictionary<string, BookingInfo>();
        foreach (var c in clients)
            foreach (var b in await api.MyBookingsAsync(c, ct))
                if (KeyOf(b) is { } key) known[key] = b;
        return known;
    }

    /// <summary>The demo key a booking was made under: the <c>[demo-...]</c> at the start of its brief.</summary>
    internal static string? KeyOf(BookingInfo b) =>
        b.BriefText is { Length: > 2 } t && t[0] == '[' && t.IndexOf(']') is > 1 and var end && DemoNames.IsDemo(t[1..end]) ? t[1..end] : null;

    // ---- find or create

    /// <summary>The offering a plan buys from an agent: its single-task package, its task pack or its Chat package, found in the catalog and then by title among the agent's offerings.</summary>
    public static DemoOffering Pick(SeededPractitioner seeded, PackagePick pick)
    {
        var demo = DemoCatalog.Agents.Single(p => p.UserName == seeded.Key);
        var offering = pick switch
        {
            PackagePick.SingleTask => demo.Packages.FirstOrDefault(o => o.Kind == DemoKind.Tasks && o.Units == 1),
            PackagePick.TaskPack => demo.Packages.FirstOrDefault(o => o.Kind == DemoKind.Tasks && o.Units > 1),
            _ => demo.Packages.FirstOrDefault(o => o.Kind == DemoKind.Chat),
        };
        return offering ?? throw new SeedException($"booking: {seeded.Key} has no {pick} package in the catalog");
    }

    async Task<(Work Work, bool Created)> BuildAsync(SeedContext context, BookingApi api, DemoBooking plan, List<SeededPractitioner> approved, List<DemoUser> clients,
        Dictionary<string, BookingInfo> known, CancellationToken ct)
    {
        DemoNames.Require(plan.Key);
        var seeded = approved[plan.Agent % approved.Count];
        var client = clients[plan.Client % clients.Count];
        var offering = Pick(seeded, plan.Package);
        var offeringId = seeded.Packages.TryGetValue(offering.Title, out var id) ? id : throw new SeedException($"booking: {seeded.Key} has no offering '{plan.Package}' in the Directory");

        if (known.TryGetValue(plan.Key, out var existing))
        {
            context.Log.Info($"booking: {plan.Key} found as {existing.Status}");
            return (new Work(plan, client, seeded, existing), false);
        }

        var outcome = await api.CreateAsync(client, plan.IdempotencyKey, offeringId, plan.BriefText, ct);
        var created = await api.GetAsync(client, outcome.Id, ct);
        context.Log.Info($"booking: {plan.Key} created {seeded.Key}");
        return (new Work(plan, client, seeded, created), true);
    }

    // ---- bring each booking forward

    /// <summary>Takes every step the booking has not yet passed: payment, the tasks, cancel or claim. Returns the booking as it is then and the writes made.</summary>
    async Task<(Work Work, int Changes)> AdvanceAsync(BookingApi api, DemoUser admin, Work w, SeedContext context, CancellationToken ct)
    {
        var changes = 0;
        var info = w.Info;
        var target = w.Plan.Target;

        if (info.Status == "PendingPayment" && target != BookingTarget.PendingPayment)
        {
            await api.SimulatePaymentAsync(w.Client, info.Id, target == BookingTarget.Expired ? "failed" : "succeeded", ct); // a failed payment moves the booking to Expired (CONTRACTS 8.1)
            info = await api.GetAsync(w.Client, info.Id, ct);
            changes++;
        }

        // the tasks: counted from UnitsUsed, so a second run orders none. Only a Confirmed package takes a task (a Completed one has used its last unit already).
        if (info.Status == "Confirmed" && info.Kind == DemoKind.Tasks)
        {
            for (var used = info.UnitsUsed; used < w.Plan.Tasks; used++)
            {
                await api.CreateTaskAsync(w.Client, info.Id, w.Plan.TaskText((int)used), ct);
                changes++;
            }
            if (changes > 0) info = await api.GetAsync(w.Client, info.Id, ct);
        }

        switch (target, info.Status)
        {
            case (BookingTarget.CancelledByClient, "Confirmed"):
                await api.CancelAsync(w.Client, info.Id, "Zmiana planów, rezygnuję z pakietu przed pierwszym użyciem.", ct); // nothing used yet: a full refund
                changes++;
                break;
            case (BookingTarget.Disputed or BookingTarget.Resolved, "Confirmed" or "Completed"): // a claim is allowed on a Confirmed or a Completed package, until its end + 72 h
                await api.DisputeAsync(w.Client, info.Id, "not_as_described", "Odpowiedź agenta odbiegała od opisu pakietu w ofercie.", ct);
                changes++;
                if (target == BookingTarget.Resolved)
                {
                    await api.ResolveAsync(admin, info.Id, "release", "Agent wykonał zadanie zgodnie z ofertą, środki dla dostawcy.", ct);
                    changes++;
                }
                break;
            case (BookingTarget.Resolved, "Disputed"): // an earlier run stopped between the claim and the decision
                await api.ResolveAsync(admin, info.Id, "release", "Agent wykonał zadanie zgodnie z ofertą, środki dla dostawcy.", ct);
                changes++;
                break;
        }
        return (w with { Info = info }, changes);
    }

    /// <summary>Polls the packages until the service's auto-complete job has completed them. Without that job it says so and goes on: the rest of the data is still valid.</summary>
    async Task WaitForCompletedAsync(SeedContext context, BookingApi api, List<Work> works, CancellationToken ct)
    {
        for (var waited = TimeSpan.Zero; ; waited += CompletionPoll)
        {
            var open = new List<string>();
            foreach (var w in works)
                if (w.Info.Status != "Completed" && (await api.GetAsync(w.Client, w.Info.Id, ct)).Status != "Completed") open.Add(w.Plan.Key);
            if (open.Count == 0) return;
            if (waited >= CompletionWait)
            {
                context.Log.Info($"booking: {open.Count} package(s) are still Confirmed after their last unit was used: no auto-complete job answered within {CompletionWait.TotalSeconds:0} s");
                return;
            }
            await Task.Delay(CompletionPoll, context.Time, ct);
        }
    }

    /// <summary>Polls the ordered tasks until the agent platform has answered them (no task is queued or running any more). Counts only: never a task text or an answer.</summary>
    async Task WaitForTasksAsync(SeedContext context, BookingApi api, List<Work> works, CancellationToken ct)
    {
        var pending = 0;
        for (var waited = TimeSpan.Zero; ; waited += CompletionPoll)
        {
            pending = 0;
            foreach (var w in works)
                pending += (await api.TasksAsync(w.Client, w.Info.Id, ct)).Count(t => t.Status is "queued" or "running");
            if (pending == 0) return;
            if (waited >= TaskWait) break;
            await Task.Delay(CompletionPoll, context.Time, ct);
        }
        context.Log.Info($"booking: {pending} task(s) are still waiting for the agent platform after {TaskWait.TotalSeconds:0} s (is the task job running?)");
    }
}
