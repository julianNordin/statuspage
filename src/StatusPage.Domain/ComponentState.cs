namespace StatusPage.Domain;

/// <summary>
/// What a component is currently believed to be doing.
/// </summary>
public enum ComponentState
{
    /// <summary>Never checked, or checked too few times for the policy to have committed.</summary>
    Unknown = 0,

    /// <summary>Answering, and answering quickly enough.</summary>
    Up = 1,

    /// <summary>Answering correctly, but slower than the component's latency budget.</summary>
    Degraded = 2,

    /// <summary>Not answering, or answering with something other than what was expected.</summary>
    Down = 3,
}
