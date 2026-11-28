using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Packages;

/// <summary>The real count behind the submit checklist item `offering`. A singleton (IChecklistFacts is one) that opens its own scope per call.</summary>
public sealed class PackageFacts(IServiceScopeFactory scopes) : IChecklistFacts
{
    public async Task<int> ActivePackageCountAsync(Guid agentId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DirectoryDbContext>();
        return await db.Set<Package>().CountAsync(o => o.AgentId == agentId && o.IsActive, ct);
    }
}
