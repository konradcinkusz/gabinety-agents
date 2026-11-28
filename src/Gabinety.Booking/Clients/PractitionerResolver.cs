using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace Gabinety.Booking.Clients;

/// <summary>
/// The answer of <see cref="PractitionerResolver.ResolveAsync"/>. <see cref="AgentId"/> is the agent the call is about; null with <see cref="Ambiguous"/> false means the user
/// owns no such agent (not a practitioner, or the named slug is not theirs); <see cref="Ambiguous"/> means they own several and named none.
/// </summary>
public readonly record struct AgentResolution([property: JsonPropertyName("practitionerId")] Guid? AgentId, bool Ambiguous);

/// <summary>
/// Maps the token's user id (and optionally an agent slug) to the practitioner id through Directory. One provider account may own several agents (E-04): without a slug the user must
/// own exactly one; with a slug it must be one of theirs. The account's agent list is cached per user for 60 s; a non-practitioner is not cached, and a slug missing from a cached
/// list is asked about again at once, because the agent may have been created within the minute.
/// </summary>
public sealed class PractitionerResolver(IDirectoryClient directory, TimeProvider time)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    readonly ConcurrentDictionary<string, (IReadOnlyList<DirectoryAgent> Agents, DateTimeOffset At)> _cache = new();

    /// <summary>Forgets every answer. The demo reset calls it: the Directory is reset and reseeded together with Booking, and the same demo user then has a new practitioner id.</summary>
    public void Clear() => _cache.Clear();

    /// <summary>Throws DirectoryUnavailableException when Directory cannot answer.</summary>
    public async Task<AgentResolution> ResolveAsync(string userId, string? agentSlug, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(agentSlug) ? null : agentSlug.Trim();
        var now = time.GetUtcNow();
        var cached = _cache.TryGetValue(userId, out var hit) && now - hit.At < Ttl;
        var agents = cached ? hit.Agents : await Load(userId, now, ct);
        var result = Pick(agents, slug);
        if (result.AgentId is null && !result.Ambiguous && cached && slug is not null)
            result = Pick(await Load(userId, now, ct), slug); // a new agent of this account is not in the cached list yet
        return result;
    }

    async Task<IReadOnlyList<DirectoryAgent>> Load(string userId, DateTimeOffset now, CancellationToken ct)
    {
        var found = await directory.GetPractitionerAgentsAsync(userId, ct);
        if (found.Count == 0) _cache.TryRemove(userId, out _); else _cache[userId] = (found, now);
        return found;
    }

    static AgentResolution Pick(IReadOnlyList<DirectoryAgent> agents, string? slug)
    {
        if (slug is not null) return new(agents.FirstOrDefault(a => a.Slug == slug)?.AgentId, false);
        return agents.Count switch { 0 => new(null, false), 1 => new(agents[0].AgentId, false), _ => new(null, true) };
    }
}
