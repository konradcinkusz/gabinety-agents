using Gabinety.Booking.Jobs.AutoComplete;
using Gabinety.Booking.Jobs.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AccessEntity = Gabinety.Booking.Features.Bookings.Entities.Access;
using AccessStatus = Gabinety.Billing.AccessStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The job restricted to one practitioner: the test InMemory database is shared by every host in the process.</summary>
sealed class ScopedAutoCompleteJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<AutoCompleteJob> log, Guid agent)
    : AutoCompleteJob(scopes, time, config, metrics, log)
{
    protected override IQueryable<AccessEntity> Scope(IQueryable<AccessEntity> q) => q.Where(b => b.AgentId == agent);
}

static class Auto
{
    /// <summary>The old completion window (48 h): it no longer applies (A-03); tests use it to show that it does not.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public static CreateHost Host(params (string Key, string Value)[] settings) => Hold.Host(null, settings);

    /// <summary>A booking in <paramref name="status"/> whose validity (one hour) ended <paramref name="endedAgo"/> before the host clock; a negative value is an end in the future. Units: 5, none used.</summary>
    public static Task<Guid> Seed(CreateHost h, TimeSpan endedAgo, AccessStatus status = AccessStatus.Confirmed, long unitsUsed = 0, long unitsTotal = 5) =>
        Life.Seed(h, status, startIn: -endedAgo - TimeSpan.FromHours(1), duration: TimeSpan.FromHours(1), unitsUsed: unitsUsed, unitsTotal: unitsTotal);

    public static ScopedAutoCompleteJob Job(CreateHost h) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock, h.Services.GetRequiredService<IConfiguration>(),
            h.Services.GetRequiredService<JobMetrics>(), h.Services.GetRequiredService<ILogger<AutoCompleteJob>>(), h.Agent);
}
