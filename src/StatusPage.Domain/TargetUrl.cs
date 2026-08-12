using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace StatusPage.Domain;

/// <summary>
/// What a monitored URL is allowed to be.
/// <para>
/// An operator supplies a URL and the server fetches it. That is the textbook shape of a
/// server-side request forgery: the attacker picks the destination, and the server brings its
/// own network position and its own identity to the request. On Azure the specific prize is
/// <c>169.254.169.254</c>, the instance metadata endpoint, which hands out the managed
/// identity's access tokens to anything inside the container that asks.
/// </para>
/// <para>
/// This type holds the half of the defence that is a pure function. The other half — resolving
/// a hostname and checking what it actually resolved to, immediately before the request —
/// lives beside the HTTP client, because a name means nothing until DNS answers.
/// </para>
/// </summary>
public static class TargetUrl
{
    /// <summary>Parses and applies every rule that does not need the network.</summary>
    /// <param name="value">What the operator typed.</param>
    /// <param name="url">The parsed URL, when it is allowed.</param>
    /// <param name="reason">Why it was refused, when it was.</param>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out Uri? url,
        out string reason)
    {
        url = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "A target URL is required.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate))
        {
            reason = "That is not a URL.";
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"The scheme '{candidate.Scheme}' is not allowed; use http or https.";
            return false;
        }

        if (!string.IsNullOrEmpty(candidate.UserInfo))
        {
            // Credentials here would be sent on every check and stored in plain text in a
            // row an operator can read back.
            reason = "A target URL may not carry credentials.";
            return false;
        }

        if (string.IsNullOrEmpty(candidate.Host))
        {
            reason = "That URL has no host.";
            return false;
        }

        // A host that is already a literal address never goes to DNS, so this is the only
        // place it can be checked. 2130706433 and 0177.0.0.1 are both 127.0.0.1 in disguise;
        // IPAddress.TryParse understands the first, and the Uri parser normalises the second.
        if (IPAddress.TryParse(candidate.Host.Trim('[', ']'), out var literal) && IsForbidden(literal))
        {
            reason = "That address is not reachable from the public internet.";
            return false;
        }

        // "localhost" is not an IP literal and would otherwise wait for DNS to say what
        // everybody already knows.
        if (candidate.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            candidate.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = "That address is not reachable from the public internet.";
            return false;
        }

        url = candidate;
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether an address is one this application must never be pointed at. Checked against
    /// every address a hostname resolves to, not only the first.
    /// </summary>
    public static bool IsForbidden(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // ::ffff:10.0.0.1 *is* 10.0.0.1. Judging it by IPv6 rules alone would let every
        // private v4 range through in a different notation.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsForbiddenV4(address),
            AddressFamily.InterNetworkV6 => IsForbiddenV6(address),

            // An address family this code has never heard of is not one to send a request to.
            _ => true,
        };
    }

    private static bool IsForbiddenV4(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        if (!address.TryWriteBytes(b, out _))
        {
            return true;
        }

        return b[0] switch
        {
            0 => true,                                   // 0.0.0.0/8, "this network"
            10 => true,                                  // 10.0.0.0/8, private
            127 => true,                                 // loopback
            169 when b[1] == 254 => true,                // link-local, incl. 169.254.169.254
            172 when b[1] >= 16 && b[1] <= 31 => true,   // 172.16.0.0/12, private
            192 when b[1] == 168 => true,                // 192.168.0.0/16, private
            100 when b[1] >= 64 && b[1] <= 127 => true,  // 100.64.0.0/10, carrier-grade NAT
            >= 224 => true,                              // multicast and reserved
            _ => false,
        };
    }

    private static bool IsForbiddenV6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
            address.IsIPv6UniqueLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        Span<byte> b = stackalloc byte[16];
        if (!address.TryWriteBytes(b, out _))
        {
            return true;
        }

        // ::/128 unspecified — IPAddress.IPv6Any covers it, but so does this, cheaply.
        var allZero = true;
        for (var i = 0; i < 16 && allZero; i++)
        {
            allZero = b[i] == 0;
        }

        return allZero;
    }
}
