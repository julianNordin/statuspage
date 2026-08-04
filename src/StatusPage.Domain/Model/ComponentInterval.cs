namespace StatusPage.Domain.Model;

/// <summary>
/// One stored stretch of time during which a component was in one state. The persisted form of
/// <see cref="StateInterval"/>, which is the pure one the uptime arithmetic works on.
/// <para>
/// A row is written when state <em>changes</em> and never when it merely repeats, so this table
/// grows with incidents rather than with time. That is the whole reason the checker can run
/// every ten minutes against a database billed by the second.
/// </para>
/// </summary>
public class ComponentInterval
{
    public long Id { get; set; }

    public Guid ComponentId { get; set; }

    public Component? Component { get; set; }

    public ComponentState State { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while this is the component's current state. At most one such row exists.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public StateInterval ToDomain() => new(State, StartedAt, EndedAt);
}
