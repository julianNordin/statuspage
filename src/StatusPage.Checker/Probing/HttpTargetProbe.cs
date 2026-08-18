using System.Diagnostics;
using StatusPage.Domain;
using StatusPage.Infrastructure.Checks;

namespace StatusPage.Checker.Probing;

/// <summary>
/// The real probe. Every request goes through the guarded connect callback, so a target that
/// resolves anywhere private is refused at the socket rather than fetched.
/// </summary>
public sealed class HttpTargetProbe(HttpClient client) : ITargetProbe
{
    /// <summary>How long a target gets to answer before it counts as down.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<CheckOutcome> ProbeAsync(string targetUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUrl);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);

        var started = Stopwatch.GetTimestamp();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);

            // HEAD would be cheaper and is answered wrongly by enough servers that a status
            // page built on it reports outages nobody else can see.
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);

            return CheckOutcome.Responded((int)response.StatusCode, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline fired, not the host shutting down.
            return CheckOutcome.TimedOut(Stopwatch.GetElapsedTime(started));
        }
        catch (HttpRequestException)
        {
            // DNS, TLS, refused — and ForbiddenTargetException arrives wrapped in one of
            // these, which is correct: a target that resolves somewhere private is a target
            // that could not be reached.
            return CheckOutcome.ConnectionFailed(Stopwatch.GetElapsedTime(started));
        }
        catch (ForbiddenTargetException)
        {
            return CheckOutcome.ConnectionFailed(Stopwatch.GetElapsedTime(started));
        }
    }
}
