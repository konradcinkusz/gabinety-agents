using System.Net;
using System.Text.Json;
using Gabinety.Billing;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Directory;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

using Access = Gabinety.Booking.Features.Bookings.Entities.Access;

public class CreateAccessApiTests
{
    const string Client = "demo-client-1";

    static async Task<CreateHost> Host(Action<CreateHost>? configure = null)
    {
        var h = new CreateHost();
        configure?.Invoke(h);
        return h;
    }

    static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_answers_201_with_the_hold_the_checkout_url_and_stores_access_payment_and_idempotency_record()
    {
        await using var h = await Host();
        var key = CreateHost.Key();
        var r = await CreateHost.Post(h.ClientFor(Client), h.Req(), key);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Body();
        var id = body.GetProperty("bookingId").GetGuid();
        Assert.Equal("PendingPayment", body.GetProperty("status").GetString());
        Assert.Equal("2026-11-16T12:10:00Z", body.GetProperty("holdExpiresAt").GetString()); // now + 10 min
        Assert.Equal($"http://localhost:3000/checkout/mock/{id}", body.GetProperty("checkoutUrl").GetString());
        Assert.Equal($"/api/v1/bookings/{id}", r.Headers.Location?.ToString());

        var b = Assert.Single(await h.Accesses());
        Assert.Equal(id, b.Id);
        Assert.Equal(AccessStatus.PendingPayment, b.Status);
        Assert.Equal((Client, "demo-practitioner-user", h.Package), (b.ClientUserId, b.AgentUserId, b.PackageId));
        Assert.Equal(("demo-offering", (short)60, 10000L), (b.PackageTitle, b.DurationMinutes, b.PriceMinor));
        Assert.Equal(h.Clock.GetUtcNow(), b.StartUtc);                 // valid from the purchase, not from a slot
        Assert.Equal(TimeSpan.FromDays(30), b.EndUtc - b.StartUtc);   // Access:ValidityDays default
        Assert.Equal((PackageKind.Tasks, 1L, 0L, "demo-practitioner"), (b.Kind, b.UnitsTotal, b.UnitsUsed, b.AgentSlug));
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 12, 10, 0, TimeSpan.Zero), b.HoldExpiresAt);
        Assert.Equal(Guid.Parse(key), b.IdempotencyKey);

        var (payment, record) = await h.Query(async db => (await db.Payments.SingleAsync(p => p.AccessId == id), await db.IdempotencyRecords.SingleAsync(x => x.ClientUserId == Client && x.IdempotencyKey == Guid.Parse(key))));
        Assert.Equal((Contracts.Booking.PaymentStatus.Created, "Mock", 10000L, 0L), (payment.Status, payment.Provider, payment.AmountMinor, payment.RefundedMinor));
        Assert.StartsWith("mock_", payment.ProviderPaymentId);
        Assert.Equal((201, 64), (record.ResponseStatus, record.RequestHash.Length));
        Assert.DoesNotContain("demo-brief", record.ResponseBody);
    }

    [Fact]
    public async Task The_same_client_may_hold_several_packages_of_one_agent_and_startUtc_is_ignored()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), CreateHost.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req("2026-12-24T03:07:00Z"), CreateHost.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateHost.Post(c, h.Req("not a date"), CreateHost.Key())).StatusCode); // malformed JSON value, not a 500
        var rows = await h.Accesses();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, b => Assert.Equal(h.Clock.GetUtcNow(), b.StartUtc));
    }

    [Fact]
    public async Task A_Chat_package_stores_tokens_and_a_Tasks_package_stores_tasks()
    {
        await using var h = await Host(x => { x.Kind = "Chat"; x.Units = 500; });
        var c = h.ClientFor(Client);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), CreateHost.Key())).StatusCode);
        var chat = Assert.Single(await h.Accesses());
        Assert.Equal((PackageKind.Chat, 500_000L, 0L), (chat.Kind, chat.UnitsTotal, chat.UnitsUsed));

        h.Kind = "Tasks"; h.Units = 7;
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), CreateHost.Key())).StatusCode);
        var tasks = (await h.Accesses()).Single(b => b.Kind == PackageKind.Tasks);
        Assert.Equal((7L, "demo-practitioner"), (tasks.UnitsTotal, tasks.AgentSlug));
    }

    [Fact]
    public async Task The_validity_follows_Access_ValidityDays()
    {
        await using var h = await Host(x => x.Settings["Access:ValidityDays"] = "7");
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key())).StatusCode);
        var b = Assert.Single(await h.Accesses());
        Assert.Equal(TimeSpan.FromDays(7), b.EndUtc - b.StartUtc);
    }

    [Fact]
    public async Task The_price_title_and_duration_are_the_snapshot_not_a_later_value()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), CreateHost.Key())).StatusCode);
        (h.PriceMinor, h.Title, h.Minutes) = (99900, "demo-renamed", 90);

        var first = Assert.Single(await h.Accesses());
        Assert.Equal((10000L, "demo-offering", (short)60), (first.PriceMinor, first.PackageTitle, first.DurationMinutes));

        // A booking created AFTER the change takes the new values: the snapshot is read at creation, not cached.
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), CreateHost.Key())).StatusCode);
        var second = (await h.Accesses()).Single(b => b.PriceMinor == 99900);
        Assert.Equal(("demo-renamed", (short)90), (second.PackageTitle, second.DurationMinutes));
        Assert.Equal(10000, (await h.Query(db => db.Payments.Where(p => p.AccessId == first.Id).Select(p => p.AmountMinor).SingleAsync())));
    }

    [Fact]
    public async Task Own_package_is_422()
    {
        await using var h = await Host(x => x.OwnerUserId = Client);
        await Problem(await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key()), (HttpStatusCode)422, "own_offering");
        Assert.Empty(await h.Accesses());
    }

    [Fact]
    public async Task An_inactive_package_is_422_package_inactive()
    {
        await using var h = await Host(x => x.Active = false);
        await Problem(await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key()), (HttpStatusCode)422, "offering_inactive");
    }

    [Theory]
    [InlineData("Suspended")]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Rejected")]
    public async Task A_agent_who_is_not_Approved_is_422_agent_not_bookable(string status)
    {
        await using var h = await Host(x => x.Status = status);
        await Problem(await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key()), (HttpStatusCode)422, "practitioner_not_bookable");
        Assert.Empty(await h.Accesses());
    }

    [Fact]
    public async Task The_check_order_is_own_package_then_agent_not_bookable_then_package_inactive()
    {
        await using var h = await Host(x => { x.OwnerUserId = Client; x.Status = "Suspended"; x.Active = false; });
        var c = h.ClientFor(Client);
        await Problem(await CreateHost.Post(c, h.Req(), CreateHost.Key()), (HttpStatusCode)422, "own_offering");
        h.OwnerUserId = "demo-someone-else";
        await Problem(await CreateHost.Post(c, h.Req(), CreateHost.Key()), (HttpStatusCode)422, "practitioner_not_bookable");
        h.Status = "Approved";
        await Problem(await CreateHost.Post(c, h.Req(), CreateHost.Key()), (HttpStatusCode)422, "offering_inactive");
    }

    [Fact]
    public async Task An_unknown_package_is_404_and_a_Directory_outage_is_503()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        await Problem(await CreateHost.Post(c, h.Req(package: Guid.NewGuid()), CreateHost.Key()), HttpStatusCode.NotFound, "not_found");
        h.Directory.Down = true;
        await Problem(await CreateHost.Post(c, h.Req(), CreateHost.Key()), HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
    }

    [Fact]
    public async Task The_same_key_and_body_twice_returns_the_original_response_and_creates_one_access_and_one_payment()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        var first = await CreateHost.Post(c, h.Req(), key);
        h.Clock.Advance(TimeSpan.FromMinutes(1)); // a re-computed answer would carry another hold time
        var second = await CreateHost.Post(c, h.Req(), key);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Single(await h.Accesses());
        var ids = (await h.Accesses()).Select(b => b.Id).ToList();
        Assert.Equal(1, await h.Query(db => db.Payments.CountAsync(p => ids.Contains(p.AccessId))));
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_409_idempotency_conflict()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), key)).StatusCode);
        await Problem(await CreateHost.Post(c, h.Req(text: "demo-brief: a completely different request text"), key), HttpStatusCode.Conflict, "idempotency_conflict");
        Assert.Single(await h.Accesses());
    }

    [Fact]
    public async Task The_body_hash_ignores_surrounding_whitespace_of_the_brief()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        var a = await CreateHost.Post(c, h.Req(text: "demo-brief: same text, twenty chars+"), key);
        var b = await CreateHost.Post(c, h.Req(text: "  demo-brief: same text, twenty chars+ \n"), key);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.Equal((await a.Body()).GetProperty("bookingId").GetGuid(), (await b.Body()).GetProperty("bookingId").GetGuid());
    }

    [Fact]
    public async Task A_different_client_with_the_same_key_gets_its_own_access()
    {
        await using var h = await Host();
        var key = CreateHost.Key();
        var a = await CreateHost.Post(h.ClientFor("demo-client-a"), h.Req(), key);
        var b = await CreateHost.Post(h.ClientFor("demo-client-b"), h.Req(), key);
        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.NotEqual((await a.Body()).GetProperty("bookingId").GetGuid(), (await b.Body()).GetProperty("bookingId").GetGuid());
        Assert.Equal(2, (await h.Accesses()).Count);
    }

    [Fact]
    public async Task A_record_older_than_24_hours_is_absent_and_the_unique_key_then_answers_409_idempotency_conflict()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(), key)).StatusCode);
        h.Clock.Advance(TimeSpan.FromHours(25));
        await Problem(await CreateHost.Post(c, h.Req(), key), HttpStatusCode.Conflict, "idempotency_conflict");
        Assert.Single(await h.Accesses());
    }

    [Fact]
    public async Task A_request_without_the_header_or_with_a_non_uuid_header_is_400_naming_the_header()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        foreach (var key in new string?[] { null, "not-a-uuid", "" })
        {
            var r = await CreateHost.Post(c, h.Req(), key);
            await Problem(r, HttpStatusCode.BadRequest, "validation_failed");
            Assert.True((await r.Body()).GetProperty("errors").TryGetProperty("Idempotency-Key", out _));
        }
        Assert.Empty(await h.Accesses());
    }

    [Fact]
    public async Task An_unauthenticated_request_is_401()
    {
        await using var h = await Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateHost.Post(h.ClientFor(null), h.Req(), CreateHost.Key())).StatusCode);
    }

    [Fact]
    public async Task The_brief_must_be_20_to_2000_characters_after_trimming()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        foreach (var (text, ok) in new[] { (new string('a', 19), false), ("  " + new string('b', 19) + "  ", false), (new string('c', 2001), false), (new string('d', 20), true) })
        {
            var r = await CreateHost.Post(c, h.Req(text: text), CreateHost.Key());
            if (!ok) { await Problem(r, (HttpStatusCode)422, "validation_failed"); Assert.True((await r.Body()).GetProperty("errors").TryGetProperty("brief.text", out _)); }
            else Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await CreateHost.Post(c, h.Req(text: new string('e', 2000)), CreateHost.Key())).StatusCode);
        Assert.Equal(2, (await h.Accesses()).Count);
    }

    [Fact]
    public async Task An_invalid_body_is_422_validation_failed_and_a_non_UTC_offset_is_refused()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        await Problem(await CreateHost.Post(c, h.Req(package: Guid.Empty), CreateHost.Key()), (HttpStatusCode)422, "validation_failed");
        await Problem(await CreateHost.Post(c, new { offeringId = h.Package, startUtc = CreateHost.Tuesday10 }, CreateHost.Key()), (HttpStatusCode)422, "validation_failed");
        Assert.Empty(await h.Accesses());
    }

    [Fact]
    public async Task The_hold_moves_with_Access_HoldMinutes_and_the_checkout_url_with_Site_BaseUrl()
    {
        await using var h = await Host(x => { x.Settings["Booking:HoldMinutes"] = "15"; x.Settings["Site:BaseUrl"] = "https://demo.example.test/"; });
        var body = await (await CreateHost.Post(h.ClientFor(Client), h.Req(), CreateHost.Key())).Body();
        Assert.Equal("2026-11-16T12:15:00Z", body.GetProperty("holdExpiresAt").GetString());
        Assert.Equal($"https://demo.example.test/checkout/mock/{body.GetProperty("bookingId").GetGuid()}", body.GetProperty("checkoutUrl").GetString());
    }

    [Fact]
    public async Task The_brief_text_is_in_no_response_and_no_log_line()
    {
        await using var h = await Host();
        var secret = "demo-brief-UNIQUE-" + Guid.NewGuid().ToString("N");
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        var bodies = new List<string>();
        foreach (var k in new[] { key, key, CreateHost.Key() })
            bodies.Add(await (await CreateHost.Post(c, h.Req(text: secret), k)).Content.ReadAsStringAsync()); // 201, replay, 201
        bodies.Add(await (await CreateHost.Post(c, h.Req(text: "x" + secret), key)).Content.ReadAsStringAsync()); // idempotency_conflict
        h.Directory.Down = true;
        bodies.Add(await (await CreateHost.Post(c, h.Req(text: secret), CreateHost.Key())).Content.ReadAsStringAsync());

        Assert.All(bodies, x => Assert.DoesNotContain(secret, x));
        Assert.NotEmpty(h.Logs.Lines);
        Assert.All(h.Logs.Lines, l => Assert.DoesNotContain(secret, l));
    }

    [Fact]
    public async Task Two_clients_buying_the_same_package_in_the_same_second_both_get_201()
    {
        await using var h = await Host();
        var a = h.ClientFor("demo-client-a");
        var b = h.ClientFor("demo-client-b");
        var rs = await Task.WhenAll(CreateHost.Post(a, h.Req(), CreateHost.Key()), CreateHost.Post(b, h.Req(), CreateHost.Key()));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(2, (await h.Accesses()).Count);
    }

    [Fact]
    public async Task Two_requests_with_the_same_key_at_once_create_one_access_and_both_answer_with_it()
    {
        await using var h = await Host();
        var c = h.ClientFor(Client);
        var key = CreateHost.Key();
        var rs = await Task.WhenAll(CreateHost.Post(c, h.Req(), key), CreateHost.Post(c, h.Req(), key));
        Assert.All(rs, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal((await rs[0].Body()).GetProperty("bookingId").GetGuid(), (await rs[1].Body()).GetProperty("bookingId").GetGuid());
        Assert.Single(await h.Accesses());
    }

    [Fact]
    public void Every_Billing_AccessStatus_exists_in_the_Contracts_enum_and_vice_versa()
    {
        var billing = Enum.GetNames<AccessStatus>().Order().ToArray();
        var contracts = Enum.GetNames<Contracts.Booking.AccessStatus>().Order().ToArray();
        Assert.Equal(contracts, billing);
        foreach (var n in billing) Assert.True(Enum.TryParse<Contracts.Booking.AccessStatus>(n, out _));
    }
}
