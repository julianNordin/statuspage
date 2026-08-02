namespace StatusPage.Domain;

/// <summary>
/// Turns one <see cref="CheckOutcome"/> into one observed <see cref="ComponentState"/>.
/// <para>
/// This is the whole of "what did that check mean?" and it is deliberately not the whole of
/// "what state is the component in?" — a single observation never moves a component on its
/// own. That is the hysteresis policy's job.
/// </para>
/// </summary>
/// <param name="ExpectedStatusCode">The one status code that counts as answering correctly.</param>
/// <param name="DegradedAbove">
/// The latency budget. A correct response that took longer than this is degraded rather than
/// down — it worked, but not well. The comparison is inclusive: a response landing exactly on
/// the budget is inside it.
/// </param>
public sealed record CheckPolicy(int ExpectedStatusCode, TimeSpan DegradedAbove)
{
    /// <summary>The latency budget. Never negative.</summary>
    public TimeSpan DegradedAbove { get; } = NotNegative(DegradedAbove);

    private static TimeSpan NotNegative(TimeSpan budget)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero, nameof(DegradedAbove));
        return budget;
    }

    public ComponentState Observe(CheckOutcome outcome)
    {
        if (outcome.Kind != CheckOutcomeKind.Responded)
        {
            return ComponentState.Down;
        }

        if (outcome.StatusCode != ExpectedStatusCode)
        {
            return ComponentState.Down;
        }

        return outcome.Latency > DegradedAbove ? ComponentState.Degraded : ComponentState.Up;
    }
}
