using System.Net;
using PdfWerk.Infrastructure.Data;

namespace PdfWerk.Tests;

/// <summary>
/// Address matching for the block list.
/// </summary>
/// <remarks>
/// Worth testing hard rather than by eye. A block that is too narrow lets abuse through and looks
/// like it is working; a block that is too wide locks out people who did nothing, and the person
/// it locks out first is often the administrator. Neither failure announces itself.
/// </remarks>
public class CidrRangeTests
{
    private static bool Blocks(string rule, string address)
    {
        Assert.True(CidrRange.TryParse(rule, out var network, out var prefix, out _), $"could not parse {rule}");
        return CidrRange.Contains(network, prefix, IPAddress.Parse(address));
    }

    [Theory]
    [InlineData("203.0.113.4", "203.0.113.4", true)]
    [InlineData("203.0.113.4", "203.0.113.5", false)]
    [InlineData("203.0.113.0/24", "203.0.113.0", true)]
    [InlineData("203.0.113.0/24", "203.0.113.255", true)]
    [InlineData("203.0.113.0/24", "203.0.114.0", false)]
    [InlineData("203.0.113.0/24", "203.0.112.255", false)]
    [InlineData("10.0.0.0/8", "10.255.255.255", true)]
    [InlineData("10.0.0.0/8", "11.0.0.0", false)]
    public void Ranges_match_exactly_the_addresses_they_should(string rule, string address, bool expected) =>
        Assert.Equal(expected, Blocks(rule, address));

    [Theory]
    [InlineData("192.168.1.0/25", "192.168.1.127", true)]
    [InlineData("192.168.1.0/25", "192.168.1.128", false)]
    [InlineData("192.168.1.128/25", "192.168.1.128", true)]
    [InlineData("192.168.1.128/25", "192.168.1.127", false)]
    public void A_prefix_that_is_not_a_whole_number_of_bytes_still_splits_where_it_should(
        string rule,
        string address,
        bool expected) =>
        Assert.Equal(expected, Blocks(rule, address));

    [Fact]
    public void Host_bits_are_cleared_so_the_same_range_is_never_two_entries()
    {
        // Someone typing their attacker's address with a /24 means the range, not that address.
        // Storing it verbatim would create a second row describing a range that already exists,
        // and unblocking one of them would appear to do nothing.
        Assert.True(CidrRange.TryParse("203.0.113.7/24", out var network, out var prefix, out _));

        Assert.Equal("203.0.113.0", network.ToString());
        Assert.Equal(24, prefix);
    }

    [Fact]
    public void A_bare_address_is_a_single_host()
    {
        Assert.True(CidrRange.TryParse("198.51.100.9", out var network, out var prefix, out var family));

        Assert.Equal("198.51.100.9", network.ToString());
        Assert.Equal(32, prefix);
        Assert.Equal(4, family);
    }

    [Theory]
    [InlineData("2001:db8::/32", "2001:db8:1234::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    [InlineData("::1", "::1", true)]
    public void IPv6_ranges_work_the_same_way(string rule, string address, bool expected) =>
        Assert.Equal(expected, Blocks(rule, address));

    [Fact]
    public void An_address_is_never_inside_a_range_of_the_other_family()
    {
        // The byte arrays are different lengths, so a naive comparison would either throw or
        // silently compare the wrong bytes and block someone unrelated.
        Assert.True(CidrRange.TryParse("10.0.0.0/8", out var network, out var prefix, out _));

        Assert.False(CidrRange.Contains(network, prefix, IPAddress.Parse("2001:db8::1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("203.0.113.0/33")]
    [InlineData("203.0.113.0/-1")]
    [InlineData("203.0.113.0/abc")]
    [InlineData("2001:db8::/129")]
    public void Nonsense_is_refused_rather_than_guessed_at(string rule) =>
        Assert.False(CidrRange.TryParse(rule, out _, out _, out _));

    [Fact]
    public void A_zero_prefix_parses_but_matches_everything()
    {
        // Parsing succeeds; it is the block list that refuses it. Kept separate on purpose: the
        // parser's job is to say what the text means, not to decide whether it is wise.
        Assert.True(CidrRange.TryParse("0.0.0.0/0", out var network, out var prefix, out _));

        Assert.Equal(0, prefix);
        Assert.True(CidrRange.Contains(network, prefix, IPAddress.Parse("8.8.8.8")));
    }
}
