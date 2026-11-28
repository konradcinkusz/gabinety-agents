using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Internal.Erasure;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

/// <summary>DELETE /internal/v1/subjects/{userId} (CONTRACTS 4.3, SPEC 5 rule 4): anonymises briefs, free texts and user ids, keeps the accounting rows.</summary>
public class ErasureApiTests
{
    internal const string InternalKey = "k-internal-DO-NOT-LEAK-9012";
    internal const string SecretBrief = "TEKST-BRIEF-NIE-DO-LOGU", SecretDispute = "TEKST-SPOR-NIE-DO-LOGU", SecretReason = "POWOD-NIE-DO-LOGU", MeetingUrl = "https://meet.demo.invalid/personal-room";

    internal static CreateHost NewHost()
    {
        var h = new CreateHost();
        h.Settings["Internal:Key"] = InternalKey;
        return h;
    }

    internal static string NewUser(string role) => $"demo-erase-{role}-{Guid.NewGuid():n}";

    /// <summary>Written out here, not taken from the code under test: "erased-" + first 40 lowercase hex characters of SHA-256 of the UTF-8 user id.</summary>
    internal static string Token(string userId) => "erased-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..40];

    internal static Task<HttpResponseMessage> Erase(CreateHost h, string userId, string? key = InternalKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/internal/v1/subjects/{userId}");
        if (key != null) req.Headers.Add("X-Internal-Key", key);
        return h.CreateClient().SendAsync(req);
    }

    internal static async Task<Guid> SeedAccess(CreateHost h, string client, string agentUser, AccessStatus status = AccessStatus.Confirmed,
        Guid? agent = null, Guid? key = null, bool withTexts = true, bool withMoney = true, int day = 1)
    {
        var id = Guid.NewGuid();
        var pid = agent ?? Guid.NewGuid();
        var start = h.Clock.GetUtcNow().AddDays(day);
        await h.Seed(db =>
        {
            db.Accesses.Add(new AccessEntity
            {
                Id = id, ClientUserId = client, AgentId = pid, AgentUserId = agentUser, PackageId = h.Package, PackageTitle = "demo-offering",
                DurationMinutes = 60, PriceMinor = 12345, StartUtc = start, EndUtc = start.AddHours(1), Status = status,
                StatusReason = withTexts ? SecretReason : null, BriefText = SecretBrief, BriefProblemSlug = "demo-problem", BriefAiAssisted = true, MeetingUrlSnapshot = MeetingUrl,
                DisputeKind = withTexts ? "other" : null, DisputeText = withTexts ? SecretDispute : null, FeeMinor = 1851, PayoutMinor = 10494,
                IdempotencyKey = key ?? Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(), ConfirmedAt = h.Clock.GetUtcNow(),
            });
            if (!withMoney) return;
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), AccessId = id, Provider = "Mock", ProviderPaymentId = "pay_" + id.ToString("N"), AmountMinor = 12345, Status = PaymentStatus.Succeeded, RefundedMinor = 0, CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow() });
            db.LedgerEntries.AddRange(
                new LedgerEntry { Id = Guid.NewGuid(), AccessId = id, Kind = LedgerKind.ClientCharge, AmountMinor = 12345, CreatedAt = h.Clock.GetUtcNow() },
                new LedgerEntry { Id = Guid.NewGuid(), AccessId = id, Kind = LedgerKind.PlatformFee, AmountMinor = 1851, PairKey = PairKey.For(client, pid), CreatedAt = h.Clock.GetUtcNow() });
            db.Payouts.Add(new Payout { Id = Guid.NewGuid(), AccessId = id, AgentId = pid, AmountMinor = 10494, Status = PayoutStatus.Pending, ProviderRef = "ref-" + id.ToString("N"), CreatedAt = h.Clock.GetUtcNow() });
        });
        return id;
    }

    static Task<AccessEntity> Row(CreateHost h, Guid id) => h.Query(db => db.Accesses.AsNoTracking().SingleAsync(b => b.Id == id));

    static Task<(int Accesses, int Records)> RowsMatching(CreateHost h, string userId) => h.Query(async db =>
        (await db.Accesses.CountAsync(b => b.ClientUserId == userId || b.AgentUserId == userId),
         await db.IdempotencyRecords.CountAsync(r => r.ClientUserId == userId)));

    /// <summary>Every money row of a booking as one string (a record of lists would compare by reference).</summary>
    static Task<string> MoneyOf(CreateHost h, Guid accessId) => h.Query(async db => string.Join("|",
        (await db.LedgerEntries.AsNoTracking().Where(l => l.AccessId == accessId).OrderBy(l => l.Id).ToListAsync()).Select(l => $"L {l.Id} {l.Kind} {l.AmountMinor} {l.CreatedAt:O}")
        .Concat((await db.Payments.AsNoTracking().Where(p => p.AccessId == accessId).ToListAsync()).Select(p => $"P {p.Id} {p.Provider} {p.ProviderPaymentId} {p.AmountMinor} {p.Status} {p.RefundedMinor}"))
        .Concat((await db.Payouts.AsNoTracking().Where(p => p.AccessId == accessId).ToListAsync()).Select(p => $"O {p.Id} {p.AgentId} {p.AmountMinor} {p.Status} {p.ProviderRef}"))));

    [Fact]
    public async Task Both_roles_of_the_erased_user_lose_the_texts_and_the_ids_and_the_old_id_matches_no_row()
    {
        using var h = NewHost();
        var user = NewUser("u"); var other = NewUser("other");
        var asClient = await SeedAccess(h, user, other);
        var asAgent = await SeedAccess(h, other, user, AccessStatus.Disputed);
        var oldKey = Guid.NewGuid();
        await h.Seed(db => db.IdempotencyRecords.Add(new IdempotencyRecord { ClientUserId = user, IdempotencyKey = oldKey, RequestHash = "h", ResponseStatus = 201, ResponseBody = "{\"id\":\"x\"}", CreatedAt = h.Clock.GetUtcNow() }));

        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);

        var c = await Row(h, asClient);
        Assert.Equal((ErasureFeature.ErasedBrief, null, null), (c.BriefText, c.StatusReason, c.DisputeText));
        Assert.Equal("[usunięto]", c.BriefText);
        Assert.Equal((Token(user), other), (c.ClientUserId, c.AgentUserId));
        var p = await Row(h, asAgent);
        Assert.Equal(("[usunięto]", null, null), (p.BriefText, p.StatusReason, p.DisputeText));
        Assert.Equal((other, Token(user)), (p.ClientUserId, p.AgentUserId));
        foreach (var b in new[] { c, p })
        {   // not personal, and the facts of the booking stay
            Assert.Equal(("demo-problem", true, 12345L), (b.BriefProblemSlug, b.BriefAiAssisted, b.PriceMinor));
            Assert.Equal((1851L, 10494L, "other"), (b.FeeMinor, b.PayoutMinor, b.DisputeKind));
        }
        Assert.Equal(AccessStatus.Confirmed, c.Status);
        Assert.Equal(AccessStatus.Disputed, p.Status);

        Assert.Equal((0, 0), await RowsMatching(h, user));
        var rec = await h.Query(db => db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.IdempotencyKey == oldKey));
        Assert.Equal((Token(user), "{\"id\":\"x\"}", 201), (rec.ClientUserId, rec.ResponseBody, rec.ResponseStatus));
    }

    [Fact]
    public async Task Ledger_payments_and_payouts_stay_and_only_the_pair_key_loses_the_client_id()
    {
        using var h = NewHost();
        var user = NewUser("u"); var other = NewUser("other"); var pid = Guid.NewGuid();
        var id = await SeedAccess(h, user, other, agent: pid);
        var before = await MoneyOf(h, id);
        Assert.Equal(4, before.Split("|").Length); // two ledger rows, the payment, the payout

        await Erase(h, user);

        Assert.Equal(before, await MoneyOf(h, id)); // ids, amounts, kinds, status, provider refs: all as they were
        var fee = await h.Query(db => db.LedgerEntries.AsNoTracking().SingleAsync(l => l.AccessId == id && l.Kind == LedgerKind.PlatformFee));
        Assert.DoesNotContain(user, fee.PairKey);
        Assert.Equal(PairKey.For(Token(user), pid), fee.PairKey); // still one per (client, practitioner), now under the opaque token
        var charge = await h.Query(db => db.LedgerEntries.AsNoTracking().SingleAsync(l => l.AccessId == id && l.Kind == LedgerKind.ClientCharge));
        Assert.Null(charge.PairKey);
        var b = await Row(h, id);
        Assert.Equal((12345L, 1851L, 10494L), (b.PriceMinor, b.FeeMinor, b.PayoutMinor));
    }

    [Fact]
    public async Task Other_users_accesses_and_the_counterparty_keep_their_own_data_and_still_see_the_access()
    {
        using var h = NewHost();
        h.OwnerUserId = NewUser("prac");
        var user = NewUser("u"); var bystander = NewUser("by");
        var shared = await SeedAccess(h, user, h.OwnerUserId, agent: h.Agent);
        var theirs = await SeedAccess(h, bystander, h.OwnerUserId, agent: h.Agent, day: 2);
        var untouchedBefore = await Row(h, theirs);

        await Erase(h, user);

        Assert.Equivalent(untouchedBefore, await Row(h, theirs), strict: true);
        Assert.Equal(SecretBrief, (await Row(h, theirs)).BriefText);
        var s = await Row(h, shared);
        Assert.Equal((h.OwnerUserId, "[usunięto]"), (s.AgentUserId, s.BriefText)); // the practitioner's own side is untouched

        var res = await h.ClientFor(h.OwnerUserId).GetAsync($"/api/v1/bookings/{shared}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("[usunięto]", json.GetProperty("brief").GetProperty("text").GetString());
        Assert.Equal(12345, json.GetProperty("price").GetProperty("amountMinor").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await h.ClientFor(user).GetAsync($"/api/v1/bookings/{shared}")).StatusCode); // the old id no longer opens it
    }

    [Fact]
    public async Task A_second_call_changes_nothing_and_an_unknown_user_answers_204_like_directory()
    {
        using var h = NewHost();
        var user = NewUser("u"); var other = NewUser("other");
        var id = await SeedAccess(h, user, other);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);
        var once = await Row(h, id);
        var money = await MoneyOf(h, id);

        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, NewUser("never-existed"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, Token(user))).StatusCode); // erasing the token itself must not rewrite the row again

        Assert.Equivalent(once, await Row(h, id), strict: true);
        Assert.Equal(money, await MoneyOf(h, id));
    }

    [Fact]
    public async Task A_user_with_more_accesses_than_one_batch_is_erased_completely()
    {
        using var h = NewHost();
        var user = NewUser("u"); var other = NewUser("other");
        var n = ErasureFeature.BatchSize * 2 + 3;
        await h.Seed(db =>
        {
            for (var i = 0; i < n; i++)
                db.Accesses.Add(new AccessEntity
                {
                    Id = Guid.NewGuid(), ClientUserId = user, AgentId = Guid.NewGuid(), AgentUserId = other, PackageId = h.Package, PackageTitle = "demo-offering",
                    DurationMinutes = 60, PriceMinor = 100, StartUtc = h.Clock.GetUtcNow().AddDays(1), EndUtc = h.Clock.GetUtcNow().AddDays(1).AddHours(1),
                    Status = AccessStatus.Completed, BriefText = SecretBrief, IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(),
                });
        });
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);
        Assert.Equal((0, 0), await RowsMatching(h, user));
        Assert.Equal(n, await h.Query(db => db.Accesses.CountAsync(b => b.ClientUserId == Token(user) && b.BriefText == "[usunięto]")));
    }

    [Fact]
    public async Task Erasing_the_agent_clears_the_meeting_link_and_the_package_title_and_the_client_still_sees_the_access()
    {
        using var h = NewHost();
        var prac = NewUser("prac"); var client = NewUser("client");
        var id = await SeedAccess(h, client, prac);

        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, prac)).StatusCode);

        var b = await Row(h, id);
        Assert.Equal((null, ErasureFeature.ErasedBrief, "[usunięto]"), (b.MeetingUrlSnapshot, b.PackageTitle, b.PackageTitle));
        Assert.Equal((client, Token(prac), "[usunięto]"), (b.ClientUserId, b.AgentUserId, b.BriefText)); // the client's id stays; the brief is erased in both roles (task rule)
        var res = await h.ClientFor(client).GetAsync($"/api/v1/bookings/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.DoesNotContain("personal-room", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Erasing_the_client_leaves_the_meeting_link_and_the_package_title_of_the_agent()
    {
        using var h = NewHost();
        var prac = NewUser("prac"); var client = NewUser("client");
        var id = await SeedAccess(h, client, prac);

        await Erase(h, client);

        var b = await Row(h, id);
        Assert.Equal((MeetingUrl, "demo-offering", prac), (b.MeetingUrlSnapshot, b.PackageTitle, b.AgentUserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Without_the_internal_key_the_endpoint_is_401_and_nothing_changes(string? key)
    {
        using var h = NewHost();
        var user = NewUser("u");
        var id = await SeedAccess(h, user, NewUser("other"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Erase(h, user, key)).StatusCode);
        var b = await Row(h, id);
        Assert.Equal((user, SecretBrief), (b.ClientUserId, b.BriefText));
    }

    [Fact]
    public async Task No_erased_text_key_or_raw_user_id_reaches_a_log_line_of_the_service()
    {
        using var h = NewHost();
        var user = NewUser("u");
        var id = await SeedAccess(h, user, NewUser("other"));
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Erase(h, user)).StatusCode);
        Assert.Equal("[usunięto]", (await Row(h, id)).BriefText); // it really ran, so a clean log is not vacuous

        var lines = h.Logs.Lines.ToArray();
        Assert.Contains(lines, l => l.StartsWith("Gabinety.Booking.Features.Internal.Erasure") && l.Contains("Bookings=1"));
        Assert.DoesNotContain(lines, l => l.Contains(SecretBrief) || l.Contains(SecretDispute) || l.Contains(SecretReason) || l.Contains(InternalKey));
        Assert.DoesNotContain(lines, l => l.StartsWith("Gabinety.") && l.Contains(user)); // the framework's own request line carries the path; ours must not
        Assert.DoesNotContain(lines, l => l.StartsWith("Gabinety.") && l.Contains(Token(user)));
    }
}
