using System.Net.Http.Json;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Contracts.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Helpers for the lifecycle tests: a host with a recording provider, bookings seeded in any status, and the cancel call.</summary>
static class Life
{
    public const string Client = "demo-client-1", Agent = "demo-practitioner-user";

    public static CreateHost Host(RecordingProvider provider, Action<CreateHost>? tweak = null)
    {
        var h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        h.Configure = s => { s.RemoveAll<IPaymentProvider>(); s.AddSingleton<IPaymentProvider>(provider); };
        tweak?.Invoke(h);
        return h;
    }

    /// <summary>A booking row in <paramref name="status"/> starting <paramref name="startIn"/> after the host clock, with its payment (and the charge when paid).</summary>
    public static async Task<Guid> Seed(CreateHost h, AccessStatus status, TimeSpan startIn, TimeSpan? duration = null, string provider = "Mock", long unitsUsed = 0, long unitsTotal = 5)
    {
        var id = Guid.NewGuid();
        var start = h.Clock.GetUtcNow() + startIn;
        var paid = status is not (AccessStatus.PendingPayment or AccessStatus.Expired);
        await h.Seed(db =>
        {
            db.Accesses.Add(new BookingEntity
            {
                Id = id, ClientUserId = Client, AgentId = h.Agent, AgentUserId = h.OwnerUserId, PackageId = h.Package,
                PackageTitle = h.Title, DurationMinutes = 60, PriceMinor = h.PriceMinor, StartUtc = start, EndUtc = start + (duration ?? TimeSpan.FromHours(1)),
                Status = status, UnitsTotal = unitsTotal, UnitsUsed = unitsUsed, BriefText = "demo-brief: a first review of my plan", IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(),
                MeetingUrlSnapshot = status == AccessStatus.Confirmed ? "https://meet.demo.invalid/room-1" : null,
            });
            db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), AccessId = id, Provider = provider, ProviderPaymentId = "pay_" + id.ToString("N"), AmountMinor = h.PriceMinor,
                Status = paid ? PaymentStatus.Succeeded : PaymentStatus.Created, CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow(),
            });
            if (paid) db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), AccessId = id, Kind = LedgerKind.ClientCharge, AmountMinor = h.PriceMinor, CreatedAt = h.Clock.GetUtcNow() });
        });
        return id;
    }

    public static Task<HttpResponseMessage> Cancel(CreateHost h, Guid id, string? as_, string? reason = null) =>
        h.ClientFor(as_).PostAsJsonAsync($"/api/v1/bookings/{id}/cancel", reason is null ? new { } : (object)new { reason });

    public static Task<BookingEntity> Row(CreateHost h, Guid id) => h.Query(db => db.Accesses.AsNoTracking().SingleAsync(b => b.Id == id));
    public static Task<Payment> PaymentOf(CreateHost h, Guid id) => h.Query(db => db.Payments.AsNoTracking().SingleAsync(p => p.AccessId == id));
    public static async Task<List<LedgerEntry>> RefundRows(CreateHost h, Guid id) => (await Ledger(h, id)).Where(l => l.Kind == LedgerKind.Refund).ToList();
    public static Task<List<LedgerEntry>> Ledger(CreateHost h, Guid id) => h.Query(db => db.LedgerEntries.AsNoTracking().Where(l => l.AccessId == id).ToListAsync());
}
