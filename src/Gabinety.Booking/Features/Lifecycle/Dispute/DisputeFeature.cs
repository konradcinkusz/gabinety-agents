using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Accesses.Entities.Access;

namespace Gabinety.Booking.Features.Lifecycle.Dispute;

/// <summary>Body of the dispute call (CONTRACTS 4.2). Both members are nullable so a missing one is a 422 of ours, not a binder error.</summary>
public sealed record DisputeRequest(string? Kind, string? Text);

/// <summary>
/// POST /api/v1/bookings/{id}/dispute (CONTRACTS 4.2, 8.1). Only the client of the booking; anyone else gets 404 (before the body is looked at).
/// The text is user free text: it is stored in <c>dispute_text</c> and appears in no log, metric, exception or response (the BookingView does not carry it).
/// The window is the precondition passed to <see cref="AccessTransitions"/>: <see cref="InWindow"/>.
/// </summary>
public sealed class DisputeFeature : IServiceFeature
{
    public const int MinTextLength = 10, MaxTextLength = 1000;
    /// <summary>A package may be disputed from the purchase until 72 h after its validity ended (A-03).</summary>
    public static readonly TimeSpan WindowAfterEnd = TimeSpan.FromHours(72);
    public static readonly string[] Kinds = ["not_as_described", "practitioner_no_show", "other"];

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings/{id:guid}/dispute", DisputeAsync).RequireAuthorization("Authenticated");

    /// <summary>
    /// A Confirmed or Completed package, for every kind of dispute, until <c>end_utc + 72 h</c> (exclusive). <paramref name="kind"/> no longer changes the window; the parameter stays
    /// so the callers and the kinds of the contract stay as they are. The state table decides which statuses may dispute at all.
    /// </summary>
    public static bool InWindow(AccessEntity b, string kind, DateTimeOffset now) =>
        b.Status is Gabinety.Billing.AccessStatus.Confirmed or Gabinety.Billing.AccessStatus.Completed && now < b.EndUtc + WindowAfterEnd;

    /// <summary>
    /// A disputed booking was money-complete: completion had set <c>fee_minor</c>, <c>payout_minor</c>, <c>payout_due_at</c> and claimed the pair's <c>PlatformFee</c> row. Nothing was paid out
    /// (the payout is due after the dispute window), so the money decision is taken again at the resolution: the amounts are cleared and the unpaid fee row is removed, which frees the pair for
    /// the booking that really earns the fee (the one deliberate delete of a ledger row, in this transaction only, never after a payout).
    /// </summary>
    static void ReopenMoney(AccessTransitionContext c) => Gabinety.Booking.Features.Payouts.MoneyReopening.Reopen(c); // shared with PackageReopening (ADR-0013)

    static async Task<IResult> DisputeAsync(Guid id, DisputeRequest? body, ClaimsPrincipal user, AccessDbContext db, AccessTransitions transitions,
        PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var ownerId = await db.Accesses.AsNoTracking().Where(b => b.Id == id).Select(b => b.ClientUserId).SingleOrDefaultAsync(ct);
        if (ownerId != sub) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // unknown, or not the client: the same answer

        var errors = new Dictionary<string, string[]>();
        var kind = body?.Kind;
        if (kind is null || !Kinds.Contains(kind)) errors["kind"] = [$"The kind is one of: {string.Join(", ", Kinds)}."];
        var text = body?.Text?.Trim();
        if (text is null || text.Length is < MinTextLength or > MaxTextLength) errors["text"] = [$"The text is {MinTextLength} to {MaxTextLength} characters."];
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null, errors);

        var now = time.GetUtcNow();
        var result = await transitions.ApplyAsync(new TransitionRequest(id, AccessTrigger.ClientDisputes, now)
        {
            Precondition = b => InWindow(b, kind!, now),
            Effects = c =>
            {
                c.Access.DisputeOpenedAt = now;
                c.Access.DisputeKind = kind;
                c.Access.DisputeText = text;
                if (c.From == Gabinety.Billing.AccessStatus.Completed) ReopenMoney(c);
            },
        }, ct);
        return result.ToErrorResult() ?? Results.Ok(AccessViewFeature.ToView(result.Access!, result.Payment, sub, admin: false, names: (await names.ForAsync([result.Access!], ct))[result.Access!.Id]));
    }
}
