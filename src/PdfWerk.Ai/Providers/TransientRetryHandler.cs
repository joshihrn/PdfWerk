using System.Net;
using Microsoft.Extensions.Logging;

namespace PdfWerk.Ai.Providers;

/// <summary>
/// Retries transient upstream failures with exponential backoff.
/// </summary>
/// <remarks>
/// <para>
/// Free model tiers shed load, and they do it often: a 503 "experiencing high demand" is a normal
/// event rather than an outage. Surfacing the first one to the caller turns a routine hiccup into
/// a failed request, so overload and rate-limit responses are retried before giving up.
/// </para>
/// <para>
/// Only responses that are safe and sensible to repeat are retried — 429, 503, 502, 504, and
/// transport-level failures. A 400 or 404 means the request itself is wrong and would fail
/// identically every time, and a 401 means the key is wrong; retrying either just wastes the
/// caller's time and the provider's quota.
/// </para>
/// </remarks>
internal sealed class TransientRetryHandler(ILogger logger, int maxAttempts = 3) : DelegatingHandler
{
    /// <summary>Ceiling on a single backoff, so a long Retry-After cannot stall the request.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Budget for one attempt. Bounded separately from the client's overall timeout because
    /// HttpClient.Timeout spans the whole pipeline, retries included: without this, one slow
    /// attempt consumes the entire budget and the retry never happens — it just cancels.
    /// A provider that has queued us for this long is shedding load anyway, so abandoning the
    /// attempt and retrying is usually faster than waiting it out.
    /// </summary>
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                response?.Dispose();

                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptTimeout.CancelAfter(PerAttemptTimeout);

                response = await base.SendAsync(request, attemptTimeout.Token).ConfigureAwait(false);

                if (attempt >= maxAttempts || !ShouldRetry(response.StatusCode))
                    return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                       && attempt < maxAttempts
                                       && !ct.IsCancellationRequested)
            {
                // A transport failure or an attempt that outran its own budget. The caller's
                // token is still live, so there is time to try again.
                response = null;
            }

            var delay = DelayFor(response, attempt);

            logger.LogInformation(
                "Upstream returned {Status}; retrying in {Delay}ms (attempt {Attempt} of {Max}).",
                response is null ? "a transport error" : ((int)response.StatusCode).ToString(),
                delay.TotalMilliseconds, attempt, maxAttempts);

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static bool ShouldRetry(HttpStatusCode status) => status is
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.BadGateway or
        HttpStatusCode.GatewayTimeout or
        HttpStatusCode.InternalServerError;

    private static TimeSpan DelayFor(HttpResponseMessage? response, int attempt)
    {
        // A provider that tells us when to come back knows better than any backoff curve.
        var retryAfter = response?.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta < MaxDelay ? delta : MaxDelay;

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until < MaxDelay ? until : MaxDelay;
        }

        // Exponential, with jitter so concurrent callers do not retry in lockstep and
        // re-create the spike that caused the shedding.
        var backoff = TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var total = backoff + jitter;

        return total < MaxDelay ? total : MaxDelay;
    }
}
