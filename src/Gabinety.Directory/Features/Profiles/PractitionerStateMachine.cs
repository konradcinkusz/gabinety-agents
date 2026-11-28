using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Features.Profiles;

public enum PractitionerTrigger { Submit, Approve, Reject, Suspend, Reinstate, NipChanged }

/// <summary>CONTRACTS 8.2 as a pure transition table; a pair that is not in the table is illegal.</summary>
public static class PractitionerStateMachine
{
    static readonly IReadOnlyDictionary<(AgentStatus, PractitionerTrigger), AgentStatus> Table =
        new Dictionary<(AgentStatus, PractitionerTrigger), AgentStatus>
        {
            [(AgentStatus.Draft, PractitionerTrigger.Submit)] = AgentStatus.Submitted,
            [(AgentStatus.Rejected, PractitionerTrigger.Submit)] = AgentStatus.Submitted,
            [(AgentStatus.Submitted, PractitionerTrigger.Approve)] = AgentStatus.Approved,
            [(AgentStatus.Submitted, PractitionerTrigger.Reject)] = AgentStatus.Rejected,
            [(AgentStatus.Approved, PractitionerTrigger.Suspend)] = AgentStatus.Suspended,
            [(AgentStatus.Suspended, PractitionerTrigger.Reinstate)] = AgentStatus.Approved,
            [(AgentStatus.Approved, PractitionerTrigger.NipChanged)] = AgentStatus.Submitted,
        };

    public static bool TryTransition(AgentStatus from, PractitionerTrigger trigger, out AgentStatus to) =>
        Table.TryGetValue((from, trigger), out to);
}
