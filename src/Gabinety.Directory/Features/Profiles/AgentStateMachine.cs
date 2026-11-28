using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Features.Profiles;

public enum AgentTrigger { Submit, Approve, Reject, Suspend, Reinstate, NipChanged }

/// <summary>CONTRACTS 8.2 as a pure transition table; a pair that is not in the table is illegal.</summary>
public static class AgentStateMachine
{
    static readonly IReadOnlyDictionary<(AgentStatus, AgentTrigger), AgentStatus> Table =
        new Dictionary<(AgentStatus, AgentTrigger), AgentStatus>
        {
            [(AgentStatus.Draft, AgentTrigger.Submit)] = AgentStatus.Submitted,
            [(AgentStatus.Rejected, AgentTrigger.Submit)] = AgentStatus.Submitted,
            [(AgentStatus.Submitted, AgentTrigger.Approve)] = AgentStatus.Approved,
            [(AgentStatus.Submitted, AgentTrigger.Reject)] = AgentStatus.Rejected,
            [(AgentStatus.Approved, AgentTrigger.Suspend)] = AgentStatus.Suspended,
            [(AgentStatus.Suspended, AgentTrigger.Reinstate)] = AgentStatus.Approved,
            [(AgentStatus.Approved, AgentTrigger.NipChanged)] = AgentStatus.Submitted,
        };

    public static bool TryTransition(AgentStatus from, AgentTrigger trigger, out AgentStatus to) =>
        Table.TryGetValue((from, trigger), out to);
}
