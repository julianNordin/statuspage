namespace StatusPage.Domain;

/// <summary>
/// Decides when a run of observations is long enough to change what a component is believed
/// to be. This is what separates a status page from an alarm that cries wolf: one failed
/// check is a blip, and a page that reports it as an outage trains people to ignore the page.
/// <para>
/// The two thresholds are deliberately separate, because the costs are not symmetric.
/// Declaring an outage that is not real is embarrassing; declaring a recovery that is not real
/// is worse, because it closes an incident somebody was still working on.
/// </para>
/// </summary>
/// <param name="FailuresToOpen">
/// Consecutive observations of a <em>worse</em> state required before it is committed.
/// </param>
/// <param name="SuccessesToClose">
/// Consecutive observations of a <em>better</em> state required before it is committed.
/// </param>
public sealed record Hysteresis(int FailuresToOpen, int SuccessesToClose)
{
    /// <summary>Consecutive worse observations required to commit. At least one.</summary>
    public int FailuresToOpen { get; } = AtLeastOne(FailuresToOpen, nameof(FailuresToOpen));

    /// <summary>Consecutive better observations required to commit. At least one.</summary>
    public int SuccessesToClose { get; } = AtLeastOne(SuccessesToClose, nameof(SuccessesToClose));

    private static int AtLeastOne(int value, string name)
    {
        // A threshold of zero commits on an observation that has not happened, so the state
        // machine would never leave whatever it was first told. That is a policy which reports
        // nothing, and it is worth refusing at construction rather than debugging later.
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, name);
        return value;
    }

    public HysteresisState Advance(HysteresisState current, ComponentState observed)
    {
        // A component nobody has checked yet has no state to defend. Waiting three checks
        // before admitting a brand-new component is down would be caution applied to nothing.
        if (current.Committed == ComponentState.Unknown)
        {
            return Commit(observed);
        }

        // The observation agrees with what we already believe: whatever run was building
        // against it is over.
        if (observed == current.Committed)
        {
            return current with { Candidate = current.Committed, ConsecutiveObservations = 0 };
        }

        // A run only counts while it argues for the same thing. Alternating between two
        // different failures is not two runs of one, it is one run of neither.
        var run = observed == current.Candidate ? current.ConsecutiveObservations + 1 : 1;

        return run >= ThresholdFor(current.Committed, observed)
            ? Commit(observed)
            : current with { Candidate = observed, ConsecutiveObservations = run };
    }

    private static HysteresisState Commit(ComponentState state) => new(state, state, 0);

    private int ThresholdFor(ComponentState from, ComponentState to) =>
        Severity(to) > Severity(from) ? FailuresToOpen : SuccessesToClose;

    /// <summary>
    /// How bad a state is, for deciding whether a transition is a worsening or a recovery.
    /// Not the enum's own value: that order is an implementation detail of the enum and this
    /// is a domain rule, so it is written down separately on purpose.
    /// </summary>
    private static int Severity(ComponentState state) => state switch
    {
        ComponentState.Up => 0,
        ComponentState.Degraded => 1,
        ComponentState.Down => 2,
        _ => -1,
    };
}
