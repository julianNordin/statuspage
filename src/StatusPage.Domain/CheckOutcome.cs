namespace StatusPage.Domain;

/// <summary>How a single check ended.</summary>
public enum CheckOutcomeKind
{
    /// <summary>The target answered. <see cref="CheckOutcome.StatusCode"/> says with what.</summary>
    Responded = 0,

    /// <summary>The target did not answer inside the timeout.</summary>
    TimedOut = 1,

    /// <summary>The connection could not be established at all — DNS, TLS, refused.</summary>
    ConnectionFailed = 2,
}

/// <summary>
/// The result of one check, before any policy is applied to it. This records what happened;
/// deciding what it <em>means</em> is <see cref="CheckPolicy"/>'s job.
/// </summary>
public readonly record struct CheckOutcome
{
    private CheckOutcome(CheckOutcomeKind kind, int? statusCode, TimeSpan latency)
    {
        Kind = kind;
        StatusCode = statusCode;
        Latency = latency;
    }

    public CheckOutcomeKind Kind { get; }

    /// <summary>The status code, when the target answered at all. Null otherwise.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// How long the check took. Meaningful for every kind: a timeout's latency is the timeout.
    /// </summary>
    public TimeSpan Latency { get; }

    public static CheckOutcome Responded(int statusCode, TimeSpan latency) =>
        new(CheckOutcomeKind.Responded, statusCode, latency);

    public static CheckOutcome TimedOut(TimeSpan after) =>
        new(CheckOutcomeKind.TimedOut, null, after);

    public static CheckOutcome ConnectionFailed(TimeSpan after) =>
        new(CheckOutcomeKind.ConnectionFailed, null, after);
}
