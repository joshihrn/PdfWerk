using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Infrastructure.Data;

namespace PdfWerk.Tests;

/// <summary>
/// Covers key issuance, validation and revocation against a real database. SQLite keeps this
/// runnable with no Docker, while still exercising the actual EF mappings and indexes rather
/// than an in-memory substitute that would hide provider-specific mistakes.
/// </summary>
public sealed class ApiKeyStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"pdfwerk-test-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public ApiKeyStoreTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddDbContextFactory<PdfWerkDbContext>(o => o.UseSqlite($"Data Source={_databasePath}"));
        services.AddSingleton<EfApiKeyStore>();

        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private EfApiKeyStore Store => _services.GetRequiredService<EfApiKeyStore>();

    private PdfWerkDbContext NewContext() =>
        _services.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>().CreateDbContext();

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A held handle is not worth failing a test over; the temp directory gets cleaned.
        }
    }

    // ---- issuance --------------------------------------------------------

    [Fact]
    public async Task Issues_a_prefixed_high_entropy_secret()
    {
        var issued = await Store.CreateAsync("my integration", QuotaTier.Free, null);

        Assert.StartsWith("pw_", issued.Secret, StringComparison.Ordinal);
        Assert.True(issued.Secret.Length > 40, $"Secret looks too short: {issued.Secret.Length} chars.");
        Assert.Equal("my integration", issued.Record.Label);
        Assert.Equal(QuotaTier.Free, issued.Record.Tier);
        Assert.Null(issued.Record.RevokedAt);
    }

    [Fact]
    public async Task Two_keys_never_collide()
    {
        var first = await Store.CreateAsync("a", QuotaTier.Free, null);
        var second = await Store.CreateAsync("b", QuotaTier.Free, null);

        Assert.NotEqual(first.Secret, second.Secret);
        Assert.NotEqual(first.Record.Id, second.Record.Id);
    }

    [Fact]
    public async Task The_secret_is_never_written_to_the_database()
    {
        // The whole point of hashing: a database dump must not yield working credentials.
        var issued = await Store.CreateAsync("leak check", QuotaTier.Free, null);

        await using var db = NewContext();
        var stored = await db.ApiKeys.AsNoTracking().SingleAsync();

        Assert.DoesNotContain(issued.Secret, stored.SecretHash, StringComparison.Ordinal);
        Assert.Equal(64, stored.SecretHash.Length);          // SHA-256, hex encoded

        // The stored prefix is a recognition aid only, and must not be enough to reconstruct it.
        Assert.True(stored.Prefix.Length <= 11);
        Assert.StartsWith(stored.Prefix, issued.Secret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_needs_a_label()
    {
        await Assert.ThrowsAsync<PdfWerkException>(() => Store.CreateAsync("   ", QuotaTier.Free, null));
    }

    // ---- validation ------------------------------------------------------

    [Fact]
    public async Task Validates_the_secret_it_issued()
    {
        var issued = await Store.CreateAsync("valid", QuotaTier.Pro, null);

        var record = await Store.ValidateAsync(issued.Secret);

        Assert.NotNull(record);
        Assert.Equal(issued.Record.Id, record!.Id);
        Assert.Equal(QuotaTier.Pro, record.Tier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-pdfwerk-key")]
    [InlineData("pw_obviouslyWrongButCorrectlyPrefixedValue0000000")]
    public async Task Rejects_anything_it_did_not_issue(string candidate)
    {
        await Store.CreateAsync("decoy", QuotaTier.Free, null);

        Assert.Null(await Store.ValidateAsync(candidate));
    }

    [Fact]
    public async Task A_revoked_key_stops_working()
    {
        var issued = await Store.CreateAsync("to revoke", QuotaTier.Free, null);
        Assert.NotNull(await Store.ValidateAsync(issued.Secret));

        await Store.RevokeAsync(issued.Record.Id);

        Assert.Null(await Store.ValidateAsync(issued.Secret));
    }

    [Fact]
    public async Task Revoking_twice_is_reported_rather_than_silently_succeeding()
    {
        var issued = await Store.CreateAsync("to revoke", QuotaTier.Free, null);
        await Store.RevokeAsync(issued.Record.Id);

        var ex = await Assert.ThrowsAsync<PdfWerkException>(() => Store.RevokeAsync(issued.Record.Id));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task An_expired_key_stops_working()
    {
        // Issued already expired, which is the same code path a key reaching its lifetime takes.
        var issued = await Store.CreateAsync("short lived", QuotaTier.Free, TimeSpan.FromSeconds(-1));

        Assert.Null(await Store.ValidateAsync(issued.Secret));
    }

    [Fact]
    public async Task A_key_with_a_lifetime_records_its_expiry()
    {
        var issued = await Store.CreateAsync("annual", QuotaTier.Free, TimeSpan.FromDays(365));

        Assert.NotNull(issued.Record.ExpiresAt);
        Assert.True(issued.Record.ExpiresAt > DateTimeOffset.UtcNow.AddDays(364));
    }

    // ---- lookup ----------------------------------------------------------

    [Fact]
    public async Task Finds_a_key_by_id()
    {
        var issued = await Store.CreateAsync("findable", QuotaTier.Free, null);

        var found = await Store.FindAsync(issued.Record.Id);

        Assert.NotNull(found);
        Assert.Equal("findable", found!.Label);
    }

    [Fact]
    public async Task Returns_nothing_for_an_unknown_id()
    {
        Assert.Null(await Store.FindAsync(Guid.NewGuid()));
    }

    // ---- record shape ----------------------------------------------------

    [Fact]
    public void An_active_record_is_recognised_as_usable()
    {
        var now = DateTimeOffset.UtcNow;

        var live = new ApiKeyRecord(Guid.NewGuid(), "x", QuotaTier.Free, now, null, null, null, 0);
        var expired = live with { ExpiresAt = now.AddMinutes(-1) };
        var revoked = live with { RevokedAt = now.AddMinutes(-1) };

        Assert.True(live.IsActive(now));
        Assert.False(expired.IsActive(now));
        Assert.False(revoked.IsActive(now));
    }
}
