using System.Net;
using PdfWerk.Core;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// One HTTP request, kept for as long as the retention setting allows.
/// </summary>
/// <remarks>
/// This is the one place in the system that holds a raw address. Everywhere else — the rate
/// limiter, the usage table — works from a salted hash, deliberately, because a counter store is
/// often shared and rarely encrypted. The log is different by necessity: an administrator cannot
/// block what they cannot read, and a hash cannot be matched against a CIDR range.
///
/// It follows that this table is the most sensitive thing the service stores. It is readable only
/// through the admin API, and <c>Admin:RetentionDays</c> exists so that "keep it forever" is a
/// decision someone makes rather than a default they inherit.
/// </remarks>
public sealed class RequestLogEntity
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The caller's address as text, IPv4 or IPv6.</summary>
    public string Address { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    /// <summary>Path only. Query strings can carry secrets and are not worth the risk.</summary>
    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public int ElapsedMs { get; set; }

    /// <summary>Truncated: a user agent is attacker-controlled and can be arbitrarily long.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Set when the caller presented a valid key.</summary>
    public Guid? ApiKeyId { get; set; }

    /// <summary>The rate-limit identity, so a row can be tied to the usage table.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The operation, when the request was one. Null for page views.</summary>
    public PdfWerkAction? Action { get; set; }

    /// <summary>True when the request was refused by the block list rather than served.</summary>
    public bool Blocked { get; set; }
}

/// <summary>
/// A blocked address or range.
/// </summary>
/// <remarks>
/// Stored as a network and a prefix length rather than as free text, so that "10.0.0.5" and
/// "10.0.0.0/8" are the same kind of thing and matching is a bitwise comparison rather than a
/// string one. A single address is simply a /32, or a /128 for IPv6.
/// </remarks>
public sealed class IpBlockEntity
{
    public Guid Id { get; set; }

    /// <summary>Canonical form, as entered: "203.0.113.4" or "203.0.113.0/24".</summary>
    public string Cidr { get; set; } = string.Empty;

    /// <summary>The network address, normalised — host bits cleared.</summary>
    public string Network { get; set; } = string.Empty;

    public int PrefixLength { get; set; }

    /// <summary>4 or 6. Kept so matching never compares across families.</summary>
    public int AddressFamily { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Which admin key added it, for accountability.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Null blocks indefinitely; otherwise the block lapses on its own.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}

/// <summary>
/// A rate limit changed from the admin portal, layered over the file configuration.
/// </summary>
/// <remarks>
/// Overrides rather than a full copy of the settings. Only the values someone actually changed
/// are stored, so anything left alone keeps following appsettings — which means a deployment can
/// still adjust defaults without an operator's one-off tweak from months ago silently winning.
/// </remarks>
public sealed class RateLimitOverrideEntity
{
    public Guid Id { get; set; }

    /// <summary>The tier this applies to.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>An action name, or empty for the tier's default limits.</summary>
    public string Action { get; set; } = string.Empty;

    public int PerMinute { get; set; }

    public int PerHour { get; set; }

    public int PerDay { get; set; }

    public int Concurrent { get; set; }

    public long MaxUploadBytes { get; set; }

    public int MaxPages { get; set; }

    public int MaxBatch { get; set; }

    public int MaxCharacters { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>Parsing and matching for CIDR ranges, shared by the store and the admin API.</summary>
public static class CidrRange
{
    /// <summary>
    /// Splits "203.0.113.0/24" into a normalised network and prefix, or returns false.
    /// </summary>
    /// <remarks>
    /// Host bits are cleared, so 203.0.113.7/24 is stored as 203.0.113.0/24. Without that, two
    /// entries describing the same range would look different and neither would match the way
    /// the person who typed them expected.
    /// </remarks>
    public static bool TryParse(string value, out IPAddress network, out int prefix, out int family)
    {
        network = IPAddress.None;
        prefix = 0;
        family = 0;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var slash = value.IndexOf('/');
        var addressPart = slash < 0 ? value.Trim() : value[..slash].Trim();

        if (!IPAddress.TryParse(addressPart, out var parsed)) return false;

        var bits = parsed.GetAddressBytes().Length * 8;

        if (slash < 0)
        {
            prefix = bits;
        }
        else if (!int.TryParse(value[(slash + 1)..].Trim(), out prefix) || prefix < 0 || prefix > bits)
        {
            return false;
        }

        network = Mask(parsed, prefix);
        family = bits == 32 ? 4 : 6;

        return true;
    }

    /// <summary>True when the address falls inside the network.</summary>
    public static bool Contains(IPAddress network, int prefix, IPAddress address)
    {
        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();

        // Never compare across families: an IPv4 address is not inside an IPv6 range, and the
        // byte arrays are different lengths anyway.
        if (networkBytes.Length != addressBytes.Length) return false;

        var whole = prefix / 8;
        var remainder = prefix % 8;

        for (var i = 0; i < whole; i++)
            if (networkBytes[i] != addressBytes[i])
                return false;

        if (remainder == 0) return true;

        var mask = (byte)(0xFF << (8 - remainder));
        return (networkBytes[whole] & mask) == (addressBytes[whole] & mask);
    }

    private static IPAddress Mask(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();

        for (var i = 0; i < bytes.Length; i++)
        {
            var bitsHere = prefix - (i * 8);

            if (bitsHere >= 8) continue;
            bytes[i] = bitsHere <= 0 ? (byte)0 : (byte)(bytes[i] & (0xFF << (8 - bitsHere)));
        }

        return new IPAddress(bytes);
    }
}
