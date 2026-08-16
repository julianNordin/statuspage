namespace StatusPage.Domain.Model;

/// <summary>
/// What the checker remembers about a component between runs: the run of observations that is
/// currently arguing for a change, and how long it is.
/// <para>
/// Kept apart from <see cref="Component"/> because it is the checker's working memory rather
/// than anything an operator configures, and apart from <see cref="ComponentInterval"/> because
/// it changes on every run while an interval is written only when state changes.
/// </para>
/// </summary>
public class CheckerState
{
    public Guid ComponentId { get; set; }

    public Component? Component { get; set; }

    /// <summary>The state the current run of observations argues for.</summary>
    public ComponentState Candidate { get; set; } = ComponentState.Unknown;

    /// <summary>How many observations in a row have argued for it.</summary>
    public int ConsecutiveObservations { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Last round-trip time in milliseconds, for the snapshot the page reads.</summary>
    public int? LastLatencyMs { get; set; }
}
