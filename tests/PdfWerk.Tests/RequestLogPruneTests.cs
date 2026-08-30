using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PdfWerk.Core;
using PdfWerk.Infrastructure.Data;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// Covers pruning on SQLite, which is where it was broken.
/// </summary>
/// <remarks>
/// ExecuteDelete cannot be translated for a DateTimeOffset comparison on SQLite: the value is
/// stored as text and the provider has no server-side notion of an offset, so the whole statement
/// is rejected. The pruner caught and logged that, which meant the log grew for ever on the
/// deployments least able to afford it and nothing said so.
/// </remarks>
public sealed class RequestLogPruneTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pdfwerk-prune-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public RequestLogPruneTests()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<PdfWerkDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private EfRequestLog Log() => new(
        _services.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>(),
        NullLogger<EfRequestLog>.Instance);

    private async Task SeedAsync(params DateTimeOffset[] times)
    {
        var factory = _services.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        foreach (var at in times)
        {
            db.RequestLog.Add(new RequestLogEntity
            {
                At = at,
                Method = "GET",
                Path = "/v1/actions",
                StatusCode = 200,
                ClientId = "ip:203.0.113.1",
                Address = "203.0.113.1",
                ElapsedMs = 4,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Old_entries_are_removed_and_recent_ones_kept()
    {
        var now = DateTimeOffset.UtcNow;

        await SeedAsync(
            now.AddDays(-100),
            now.AddDays(-95),
            now.AddDays(-1),
            now);

        var log = Log();
        var removed = await log.PruneAsync(now.AddDays(-30));

        Assert.Equal(2, removed);
        Assert.Equal(2, await log.CountAsync());
    }

    [Fact]
    public async Task Pruning_an_empty_window_removes_nothing()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(now, now.AddMinutes(-5));

        var removed = await Log().PruneAsync(now.AddYears(-1));

        Assert.Equal(0, removed);
        Assert.Equal(2, await Log().CountAsync());
    }

    [Fact]
    public async Task A_backlog_larger_than_one_batch_is_fully_pruned()
    {
        var now = DateTimeOffset.UtcNow;

        // Over the 500-row batch size, so the loop has to run more than once. A single pass
        // would leave rows behind and report a count that looked plausible.
        await SeedAsync([.. Enumerable.Range(0, 1200).Select(i => now.AddDays(-100).AddSeconds(i))]);

        var removed = await Log().PruneAsync(now.AddDays(-30));

        Assert.Equal(1200, removed);
        Assert.Equal(0, await Log().CountAsync());
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A held handle on Windows is not worth failing a passing test over.
        }
    }
}
