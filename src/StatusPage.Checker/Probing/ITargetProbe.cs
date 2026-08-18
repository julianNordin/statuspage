using StatusPage.Domain;

namespace StatusPage.Checker.Probing;

/// <summary>
/// Makes one request to a target and reports what happened.
/// <para>
/// It takes a URL rather than a component, because that is all it needs and because the cycle
/// now works from the configuration document rather than from database entities. An interface
/// so the cycle around it can be tested without a network: everything interesting about a
/// checker is a function of outcomes, and outcomes are easier to arrange than servers.
/// </para>
/// </summary>
public interface ITargetProbe
{
    Task<CheckOutcome> ProbeAsync(string targetUrl, CancellationToken cancellationToken);
}
