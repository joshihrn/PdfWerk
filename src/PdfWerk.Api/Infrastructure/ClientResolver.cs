using System.Security.Cryptography;
using System.Text;
using PdfWerk.Core.Limits;

namespace PdfWerk.Api.Infrastructure;

public sealed class ClientOptions
{
    public const string SectionName = "Client";

    /// <summary>
    /// Trust X-Forwarded-For when resolving the caller's address. Only enable behind a proxy
    /// you control: otherwise any caller can forge the header and mint unlimited identities.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>
    /// Salt mixed into the address hash. Set this in production — an unsalted hash of an IPv4
    /// address is trivially reversible by brute force, since there are only four billion of them.
    /// </summary>
    public string AddressSalt { get; set; } = string.Empty;
}

/// <summary>
/// Works out who is calling, for quota purposes.
/// </summary>
/// <remarks>
/// Anonymous callers are keyed by a salted hash of their address rather than the address itself,
/// so the counter store never holds a raw IP. That keeps a rate-limit backend — which is not a
/// secure store and may be shared or dumped — free of personal data.
/// </remarks>
public sealed class ClientResolver(Microsoft.Extensions.Options.IOptions<ClientOptions> options)
{
    private readonly ClientOptions _options = options.Value;

    public ClientIdentity Resolve(HttpContext context)
    {
        // API keys arrive here once the key store lands; until then every caller is anonymous.
        return ClientIdentity.Anonymous(HashAddress(AddressOf(context)));
    }

    private string AddressOf(HttpContext context)
    {
        if (_options.TrustForwardedHeaders &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            // The left-most entry is the original client; the rest are proxies.
            var first = forwarded.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first.Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string HashAddress(string address)
    {
        var bytes = Encoding.UTF8.GetBytes(_options.AddressSalt + '|' + address);
        var hash = SHA256.HashData(bytes);

        // Sixteen hex characters is ample to avoid collisions between concurrent callers while
        // keeping the counter keys short.
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
