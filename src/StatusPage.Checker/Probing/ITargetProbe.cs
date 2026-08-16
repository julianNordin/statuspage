using StatusPage.Domain;
using StatusPage.Domain.Model;

namespace StatusPage.Checker.Probing;

/// <summary>
/// Makes one request to a component's target and reports what happened.
/// <para>
/// An interface so the cycle around it can be tested without a network. Everything
/// interesting about the checker — hysteresis, transitions, opening an incident — is a
/// function of outcomes, and outcomes are far easier to arrange than servers.
/// </para>
/// </summary>
public interface ITargetProbe
{
    Task<CheckOutcome> ProbeAsync(Component component, CancellationToken cancellationToken);
}
