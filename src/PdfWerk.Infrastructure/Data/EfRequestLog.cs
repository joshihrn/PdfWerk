using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Infrastructure.Data;

/// <summary>
/// Buffers request records and writes them in batches.
/// </summary>
/// <remarks>
/// A row per request, written inline, would put a database round trip on the latency of every
/// call — including the ones that were refused, which are the ones an attacker sends most of. So
/// entries go into a bounded queue and a background loop drains them in batches.
///
/// The queue is bounded on purpose. Under a flood, the choice is between dropping audit rows and
/// exhausting memory, and dropping rows is plainly the better failure: the block list still works
/// without them, and the alternative takes the whole service down. Drops are counted and logged
/// so the gap is visible rather than silent.
/// </remarks>
public sealed class EfRequestLog(
    IDbContextFactory<PdfWerkDbContext> factory,
    ILogger<EfRequestLog> logger) : IRequestLog, IDisposable
{
    /// <summary>Roughly a second of sustained traffic at a rate no free tier permits.</summary>
    private const int QueueCapacity = 10_000;

    private const int BatchSize = 200;

    private readonly ConcurrentQueue<RequestLogEntity> pending = new();
    private readonly CancellationTokenSource shutdown = new();

    private int queued;
    private long dropped;
    private Task? drain;

    public void Record(RequestLogEntry entry)
    {
        if (Interlocked.Increment(ref queued) > QueueCapacity)
        {
            Interlocked.Decrement(ref queued);

            // Logged once every thousand, not every time: a flood that overruns the queue would
            // otherwise flood the log with complaints about flooding the log.
            if (Interlocked.Increment(ref dropped) % 1_000 == 1)
                logger.LogWarning("Request log queue is full; {Dropped} entries dropped so far.", dropped);

            return;
        }

        pending.Enqueue(new RequestLogEntity
        {
            At = DateTimeOffset.UtcNow,
            Address = Truncate(entry.Address, 45),
            Method = Truncate(entry.Method, 10),
            Path = Truncate(entry.Path, 512),
            StatusCode = entry.StatusCode,
            ElapsedMs = entry.ElapsedMs,
            UserAgent = entry.UserAgent is null ? null : Truncate(entry.UserAgent, 512),
            ApiKeyId = entry.ApiKeyId,
            ClientId = Truncate(entry.ClientId, 80),
            Action = entry.Action,
            Blocked = entry.Blocked,
        });

        drain ??= Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), shutdown.Token).ConfigureAwait(false);
                await FlushAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // An audit trail that takes the service down with it is worse than a gap in it.
                logger.LogError(ex, "Could not write request log entries.");
            }
        }

        await FlushAsync().ConfigureAwait(false);
    }

    private async Task FlushAsync()
    {
        var batch = new List<RequestLogEntity>(BatchSize);

        while (batch.Count < BatchSize && pending.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref queued);
            batch.Add(entry);
        }

        if (batch.Count == 0) return;

        await using var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);

        context.RequestLog.AddRange(batch);
        await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RequestLogRecord>> RecentAsync(
        int take,
        string? address = null,
        CancellationToken ct = default)
    {
        // Flushed first so "the last hundred requests" includes the one that just asked for them.
        // Without this the newest rows are still sitting in the queue and the view looks stale.
        await FlushAsync().ConfigureAwait(false);

        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = context.RequestLog.AsNoTracking().OrderByDescending(l => l.Id).AsQueryable();

        if (!string.IsNullOrWhiteSpace(address))
            query = query.Where(l => l.Address == address);

        var rows = await query.Take(Math.Clamp(take, 1, 1_000)).ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(l => new RequestLogRecord(
            l.Id,
            l.At,
            l.Address,
            l.Method,
            l.Path,
            l.StatusCode,
            l.ElapsedMs,
            l.UserAgent,
            l.ClientId,
            l.Action?.ToString(),
            l.Blocked)).ToList();
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.RequestLog.LongCountAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await context.RequestLog
            .Where(l => l.At < olderThan)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        shutdown.Cancel();

        // Waited on briefly so a graceful shutdown keeps the rows already queued. A hard kill
        // still loses them, which the contract allows.
        try
        {
            drain?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation on the way out is expected.
        }

        shutdown.Dispose();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
