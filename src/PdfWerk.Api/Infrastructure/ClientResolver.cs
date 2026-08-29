using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;

namespace PdfWerk.Api.Infrastructure;

public sealed class ClientOptions
{
    public const string SectionName = "Client";

    /// <summary>
    /// Trust X-Forwarded-For when resolving the caller's address. Only enable behind a proxy
    /// you control: otherwise any caller can forge the header and mint unlimited identities,
    /// which defeats anonymous rate limiting entirely.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>
    /// Salt mixed into the address hash. Set this in production — an unsalted hash of an IPv4
    /// address is trivially reversible by brute force, since there are only four billion of them.
    /// </summary>
    public string AddressSalt { get; set; } = string.Empty;
}

/// <summary>
/// Works out who is calling, and therefore which quota tier applies.
/// </summary>
/// <remarks>
/// Anonymous callers are keyed by a salted hash of their address rather than the address itself,
/// so the counter store never holds a raw IP. That matters because a rate-limit backend is not a
/// secure store: it is often shared, rarely encrypted, and readily dumped.
/// </remarks>
public sealed class ClientResolver(IApiKeyStore keys, IOptions<ClientOptions> options)
{
    private readonly ClientOptions _options = options.Value;

    public async Task<ClientIdentity> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        var presented = ReadKey(context.Request);

        if (!string.IsNullOrEmpty(presented))
        {
            var record = await keys.ValidateAsync(presented, ct).ConfigureAwait(false);

            if (record is not null)
            {
                // Keyed callers are counted per key, so one user's quota follows them across
                // addresses instead of being shared with everyone behind the same NAT.
                return new ClientIdentity($"key:{record.Id:N}", record.Tier, record.Id, record.Label);
            }

            // An unrecognised key falls back to anonymous rather than failing the request: a
            // stale key should still get the free tier, not a wall.
        }

        return ClientIdentity.Anonymous(HashAddress(AddressOf(context)));
    }

    /// <summary>True when a key was presented but did not resolve, so the caller can be told.</summary>
    public async Task<bool> HasInvalidKeyAsync(HttpContext context, CancellationToken ct = default)
    {
        var presented = ReadKey(context.Request);
        if (string.IsNullOrEmpty(presented))
            return false;

        return await keys.ValidateAsync(presented, ct).ConfigureAwait(false) is null;
    }

    /// <summary>Accepts either a bearer token or the simpler X-Api-Key header.</summary>
    private static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct.ToString().Trim();

        var authorization = request.Headers.Authorization.ToString();

        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
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
