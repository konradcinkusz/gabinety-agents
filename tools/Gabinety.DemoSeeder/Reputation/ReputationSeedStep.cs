namespace Gabinety.DemoSeeder;

/// <summary>
/// The Reputation data (T-08.01c, D-01): one synthetic review on each used demo package (<c>UnitsUsed &gt; 0</c>, the rule of PLAN A-03), written by the client who made the booking through the public
/// <c>POST /api/v1/reviews</c>. Reputation asks Booking whether the booking is reviewable, runs its publication rules and updates the rating summary itself, so the seeder
/// writes nothing else. Idempotent: Reputation allows one review per booking (409 already_reviewed), which this step reads as "already there".
/// Why only three: the plan reviews the three Completed demo packages; the others that are used (a task pack, the claims) stay unreviewed. The Directory baseline (DemoCatalog stats) is derived from exactly these three,
/// so a profile never shows more reviews than exist; Reputation's own summaries push with version 1 for a practitioner's first review, which the Directory ignores because its baseline is version 1 too (T-03.08 is monotonic), and says the same.
/// </summary>
public sealed class ReputationSeedStep : ISeedStep
{
    public string Name => "reputation";
    public int Order => 300;

    public async Task<int> ResetAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.ReputationUrl.Length == 0) return -1;
        if (context.Options.ReputationKey.Length == 0) throw new SeedException("reputation: Internal:Keys:Reputation is not configured");
        return await new ReputationApi(context.Http.For(SeedService.Reputation), context.Time, context.Options.ReputationKey).ResetAsync(ct);
    }

    public async Task RunAsync(SeedContext context, CancellationToken ct)
    {
        if (context.Options.ReputationUrl.Length == 0) { context.Log.Info("reputation: skipped (Backend:Reputation is not configured)"); return; }
        if (context.Options.ReputationKey.Length == 0) throw new SeedException("reputation: Internal:Keys:Reputation is not configured");
        if (context.Options.AccessUrl.Length == 0) throw new SeedException("reputation: Backend:Booking is not configured (reviews need the bookings)");
        var accesses = new BookingApi(context.Http.For(SeedService.Access), context.Options, context.Time);
        var reputation = new ReputationApi(context.Http.For(SeedService.Reputation), context.Time);
        var clients = context.Clients.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Value).ToList();
        if (clients.Count == 0) throw new SeedException("reputation: no demo clients (run the directory step first)");

        // the used demo packages (a review needs UnitsUsed above 0, and a package that is Confirmed, Completed or Resolved), each with the client who made it
        var completed = new Dictionary<string, (DemoUser Client, Guid Id)>();
        foreach (var client in clients)
            foreach (var b in await accesses.MyAccessesAsync(client, ct))
                if (b.Status is "Confirmed" or "Completed" or "Resolved" && b.UnitsUsed > 0 && BookingSeedStep.KeyOf(b) is { } key) completed[key] = (client, b.Id);
        if (completed.Count == 0)
        {
            context.Log.Info("reputation: no used demo package to review (the Booking step orders the tasks through the API)");
            return;
        }

        var changes = 0;
        foreach (var review in ReputationPlan.All)
        {
            DemoNames.Require(review.Key);
            if (!completed.TryGetValue(review.BookingKey, out var access))
            {
                context.Log.Info($"reputation: {review.Key} skipped, {review.BookingKey} is not a used package");
                continue;
            }
            var (outcome, status) = await reputation.CreateReviewAsync(access.Client, access.Id, review.Rating, review.Text, ct);
            switch (outcome)
            {
                case ReviewOutcome.Created:
                    changes++;
                    context.Log.Info($"reputation: {review.Key} created {status}");
                    break;
                case ReviewOutcome.AlreadyReviewed:
                    context.Log.Info($"reputation: {review.Key} found");
                    break;
                case ReviewOutcome.NotReviewable:
                    context.Log.Info($"reputation: {review.Key} skipped, {review.BookingKey} is past its review window");
                    break;
            }
        }
        context.Log.Info($"reputation: {ReputationPlan.All.Length} demo reviews {(changes == 0 ? "unchanged" : $"changes={changes}")}");
    }
}
