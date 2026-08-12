using System.Net;
using System.Net.Sockets;
using StatusPage.Domain;

namespace StatusPage.Infrastructure.Checks;

/// <summary>
/// Raised when a target resolved to somewhere the checker must not go.
/// </summary>
public sealed class ForbiddenTargetException : Exception
{
    public ForbiddenTargetException(string message) : base(message) { }

    public ForbiddenTargetException(string message, Exception innerException)
        : base(message, innerException) { }

    public ForbiddenTargetException() : base("The target resolved to a forbidden address.") { }
}

/// <summary>
/// Resolves a host, refuses every address it must not reach, and then connects to one of the
/// addresses it actually checked.
/// <para>
/// Connecting to the checked address is the whole point, and it is why this is a connect
/// callback rather than a validation step before the request. Validating a hostname and then
/// handing the name to the HTTP stack leaves a window in which DNS can answer differently the
/// second time — a public address for the check, a private one for the connection. The window
/// is small and entirely sufficient; closing it costs nothing here.
/// </para>
/// </summary>
public static class GuardedConnect
{
    /// <summary>Builds a handler whose connections are restricted to public addresses.</summary>
    public static SocketsHttpHandler CreateHandler(TimeSpan connectTimeout) =>
        new()
        {
            // A redirect is the oldest way past a destination check: the target is public,
            // the Location is not. Redirects are not followed, and a 3xx is simply the
            // response the check compares against its expected status.
            AllowAutoRedirect = false,
            ConnectTimeout = connectTimeout,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectCallback = ConnectAsync,
        };

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        // Every address, not the first. A name that resolves to one public address and one
        // private one is a name that must not be used at all.
        var allowed = Array.FindAll(addresses, a => !TargetUrl.IsForbidden(a));

        if (allowed.Length != addresses.Length || allowed.Length == 0)
        {
            throw new ForbiddenTargetException(
                $"'{host}' resolves to an address that is not reachable from the public internet.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(
                new IPEndPoint(allowed[0], context.DnsEndPoint.Port), cancellationToken)
                .ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
