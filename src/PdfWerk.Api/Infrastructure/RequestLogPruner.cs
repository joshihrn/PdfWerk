using PdfWerk.Core.Abstractions;

namespace PdfWerk.Api.Infrastructure;

/// <summary>
/// Deletes request log rows past the retention window.
/// </summary>
/// <remarks>
/// Off by default, because the operator asked for an indefinite record and that should be what
/// they get unless they say otherwise. It exists because an unbounded table of addresses is both
/// a growing storage cost and a growing liability — addresses are personal data in the UK and EU,
/// and "we kept everything forever" is a poor answer to why.
/// </remarks>
public sealed class RequestLogPruner(
    IRequestLog log,
    int retentionDays,
    ILogger<RequestLogPruner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (retentionDays <= 0) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await log
                    .PruneAsync(DateTimeOffset.UtcNow.AddDays(-retentionDays), stoppingToken)
                    .ConfigureAwait(false);

                if (removed > 0)
                    logger.LogInformation("Pruned {Removed} request log rows older than {Days} days.", removed, retentionDays);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not prune the request log.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken).ConfigureAwait(false);
        }
    }
}
