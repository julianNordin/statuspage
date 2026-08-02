namespace StatusPage.Domain;

/// <summary>
/// Everything the hysteresis policy needs to remember between checks: what the component is
/// currently believed to be, what a run of recent observations is arguing for instead, and how
/// long that run is.
/// </summary>
/// <param name="Committed">
/// The state the component is actually in, as far as the rest of the system is concerned.
/// </param>
/// <param name="Candidate">
/// The state the current run of observations is arguing for. Equal to <paramref name="Committed"/>
/// when there is no argument in progress.
/// </param>
/// <param name="ConsecutiveObservations">
/// How many observations in a row have argued for <paramref name="Candidate"/>. Zero when the
/// last observation confirmed the committed state.
/// </param>
public readonly record struct HysteresisState(
    ComponentState Committed,
    ComponentState Candidate,
    int ConsecutiveObservations)
{
    /// <summary>A component that has never been checked. The first observation commits.</summary>
    public static HysteresisState NeverChecked { get; } =
        new(ComponentState.Unknown, ComponentState.Unknown, 0);
}
