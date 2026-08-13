namespace StatusPage.Domain;

/// <summary>Where an incident has got to.</summary>
public enum IncidentStatus
{
    /// <summary>Something is wrong and nobody knows why yet.</summary>
    Investigating = 0,

    /// <summary>The cause is understood; the fix is not finished.</summary>
    Identified = 1,

    /// <summary>A fix is in and is being watched.</summary>
    Monitoring = 2,

    /// <summary>Over. Terminal — see <see cref="IncidentLifecycle"/>.</summary>
    Resolved = 3,
}

/// <summary>How much of a problem an incident is for the people reading about it.</summary>
public enum IncidentImpact
{
    /// <summary>Nothing a user would notice — a maintenance note, or a near miss.</summary>
    None = 0,
    Minor = 1,
    Major = 2,
    Critical = 3,
}

/// <summary>
/// Which status changes an incident is allowed to make.
/// <para>
/// The interesting rule is that <see cref="IncidentStatus.Resolved"/> is terminal. Reopening
/// an incident rewrites what a reader was already told, and the honest version of "it came
/// back" is a second incident. That is also what the uptime arithmetic wants, because the gap
/// between the two was genuinely up and a reopened incident would swallow it.
/// </para>
/// </summary>
public static class IncidentLifecycle
{
    /// <summary>Whether an incident in <paramref name="from"/> may be moved to <paramref name="to"/>.</summary>
    public static bool CanMoveTo(IncidentStatus from, IncidentStatus to)
    {
        // An update that does not change the status is the commonest kind: "still working
        // on it". Same-to-same is always allowed, including Resolved to Resolved.
        if (from == to)
        {
            return true;
        }

        // Everything else out of Resolved is refused.
        return from != IncidentStatus.Resolved;
    }

    /// <summary>Whether an incident still needs somebody's attention.</summary>
    public static bool IsOpen(IncidentStatus status) => status != IncidentStatus.Resolved;
}
