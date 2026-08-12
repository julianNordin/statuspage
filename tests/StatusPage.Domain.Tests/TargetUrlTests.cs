using System.Net;

namespace StatusPage.Domain.Tests;

/// <summary>
/// What an operator is allowed to point a check at. An operator supplies a URL and the server
/// then fetches it, which is the textbook shape of a server-side request forgery: the attacker
/// chooses the destination and the server brings its own network position and its own identity.
/// </summary>
public class TargetUrlTests
{
    [Theory]
    [InlineData("https://example.com/health")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com:8443/status")]
    [InlineData("https://sub.domain.example.com/a/b?c=d")]
    public void An_ordinary_public_url_is_allowed(string url)
    {
        Assert.True(TargetUrl.TryParse(url, out _, out _));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("gopher://example.com")]
    [InlineData("data:text/plain,hello")]
    [InlineData("jar:http://example.com!/")]
    public void Only_http_and_https_are_allowed(string url)
    {
        Assert.False(TargetUrl.TryParse(url, out _, out var reason));
        Assert.Contains("scheme", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    public void Something_that_is_not_a_url_is_refused(string url)
    {
        Assert.False(TargetUrl.TryParse(url, out _, out _));
    }

    [Fact]
    public void A_url_carrying_credentials_is_refused()
    {
        // Credentials in a target would be sent by the checker on every run, and stored in
        // plain text in a row an operator can read back.
        Assert.False(TargetUrl.TryParse("https://user:secret@example.com/", out _, out var reason));
        Assert.Contains("credential", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    public void An_address_inside_the_network_is_forbidden(string address)
    {
        Assert.True(TargetUrl.IsForbidden(IPAddress.Parse(address)));
    }

    [Fact]
    public void The_cloud_metadata_address_is_forbidden()
    {
        // 169.254.169.254 is the instance metadata endpoint on Azure, AWS and GCP alike.
        // Reaching it from inside a container hands out the managed identity's access tokens,
        // which is the single most valuable thing this application could be tricked into
        // fetching. It is link-local, so the general rule already covers it — this test names
        // it anyway, because it is the one address that must never stop being covered.
        Assert.True(TargetUrl.IsForbidden(IPAddress.Parse("169.254.169.254")));
    }

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.255.255")]
    public void Link_local_addresses_are_forbidden(string address)
    {
        Assert.True(TargetUrl.IsForbidden(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::")]
    public void The_same_rules_apply_over_ipv6(string address)
    {
        Assert.True(TargetUrl.IsForbidden(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void An_ipv4_address_wearing_an_ipv6_costume_is_still_forbidden(string address)
    {
        // ::ffff:10.0.0.1 is 10.0.0.1. Checking the v6 form against v6 rules only would let
        // every private v4 range through in a different notation.
        Assert.True(TargetUrl.IsForbidden(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void A_genuinely_public_address_is_allowed(string address)
    {
        Assert.False(TargetUrl.IsForbidden(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://2130706433/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    public void A_literal_address_that_is_forbidden_is_refused_at_parse_time(string url)
    {
        // 2130706433 is 127.0.0.1 written as a single decimal, and 0177.0.0.1 is the same in
        // octal. A host that is already an address never reaches DNS, so it has to be caught
        // here or not at all.
        Assert.False(TargetUrl.TryParse(url, out _, out _));
    }

    [Fact]
    public void A_hostname_is_allowed_at_parse_time_because_only_dns_can_settle_it()
    {
        // Parsing cannot know where a name points. The resolver checks every address the name
        // resolves to, immediately before the request is made.
        Assert.True(TargetUrl.TryParse("https://example.com/health", out _, out _));
    }
}
