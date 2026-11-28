using System.Buffers.Text;
using System.Text;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Analytics;

/// <summary>
/// <c>GET /internal/v1/analytics/pair-counts?minBookings=3&amp;limit=&amp;cursor=</c> (T-10.03, SPEC 11), behind the "Internal" policy, asked by Reputation for the
/// administrator's list of suspicious pairs. It answers, per (client, practitioner) pair, how many PAID bookings the pair has; Reputation joins that with its reviews.
/// A booking counts when it is PAID AND KEPT: it has a payment with status <c>Succeeded</c> or <c>PartiallyRefunded</c>. A booking whose payment was never made
/// (none, <c>Created</c>, <c>Failed</c>) or was refunded in full costs the farmer nothing and does not count; the booking's own status does not matter.
/// Privacy: ids, the fee hook's <see cref="PairKey"/> and a count; no brief, no price, no time. Pages in the total order (client id, practitioner id), at most
/// <see cref="MaxLimit"/> a page, with an opaque cursor.
/// </summary>
public sealed class PairCountsFeature : IServiceFeature
{
    public const int DefaultMinAccesses = 3, MinMinAccesses = 2, MaxMinAccesses = 1000, DefaultLimit = 20, MaxLimit = 50;

    static readonly PaymentStatus[] Kept = [PaymentStatus.Succeeded, PaymentStatus.PartiallyRefunded];

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/internal/v1/analytics/pair-counts", ListAsync).RequireAuthorization("Internal");

    static async Task<IResult> ListAsync([Microsoft.AspNetCore.Mvc.FromQuery(Name = "minBookings")] string? minAccesses, string? cursor, int? limit, AccessDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var min = DefaultMinAccesses;
        if (minAccesses is not null && !(int.TryParse(minAccesses, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out min) && min is >= MinMinAccesses and <= MaxMinAccesses))
            errors["minBookings"] = [$"minBookings is {MinMinAccesses} to {MaxMinAccesses}."];
        if (limit is < 1) errors["limit"] = ["The limit is at least 1."];
        string afterClient = ""; Guid afterAgent = default; var hasCursor = false;
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out afterClient, out afterAgent)) errors["cursor"] = ["The cursor is not valid."];
            else hasCursor = true;
        }
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors);

        var take = Math.Min(limit ?? DefaultLimit, MaxLimit);
        var q = db.Accesses.AsNoTracking()
            .Where(b => db.Payments.Any(p => p.AccessId == b.Id && Kept.Contains(p.Status)));
        if (hasCursor)
            q = q.Where(b => string.Compare(b.ClientUserId, afterClient) > 0 || (b.ClientUserId == afterClient && b.AgentId.CompareTo(afterAgent) > 0));
        var rows = await q.GroupBy(b => new { b.ClientUserId, b.AgentId })
            .Select(g => new { g.Key.ClientUserId, g.Key.AgentId, Count = g.Count() })
            .Where(g => g.Count >= min)
            .OrderBy(g => g.ClientUserId).ThenBy(g => g.AgentId)
            .Take(take + 1).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);
        var items = rows.Select(r => new PairCountDto(PairKey.For(r.ClientUserId, r.AgentId), r.ClientUserId, r.AgentId, r.Count)).ToList();
        return Results.Json(new Page<PairCountDto>(items, more ? Encode(rows[^1].ClientUserId, rows[^1].AgentId) : null));
    }

    // base64url(client) . practitioner(N): the client id is arbitrary text, base64url has no '.', so the split is unambiguous.
    static string Encode(string client, Guid agent) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(client)) + "." + agent.ToString("N");

    static bool TryDecode(string cursor, out string client, out Guid agent)
    {
        client = ""; agent = default;
        var parts = cursor.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || !Guid.TryParseExact(parts[1], "N", out agent)) return false;
        try { client = new UTF8Encoding(false, true).GetString(Base64Url.DecodeFromChars(parts[0])); return client.Length > 0; }
        catch (Exception e) when (e is FormatException or ArgumentException) { return false; }
    }
}
