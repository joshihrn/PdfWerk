using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Infrastructure.Data;
using PdfWerk.Infrastructure.RateLimiting;
using StackExchange.Redis;

namespace PdfWerk.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Wires storage and rate limiting, choosing backends from the connection strings present.
    /// </summary>
    /// <remarks>
    /// Both subsystems degrade to a single-process implementation when their service is not
    /// configured, so the whole application runs with no infrastructure at all — which is what
    /// makes local development and self-hosting practical. The production choices (Redis and
    /// Postgres) are opt-in by connection string rather than by build configuration, so the same
    /// binary serves both.
    /// </remarks>
    public static IServiceCollection AddPdfWerkInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddRateLimiting(services, configuration);
        AddStorage(services, configuration);

        return services;
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var config = ConfigurationOptions.Parse(redis);

            // Never block startup on Redis: the host should come up and report unhealthy rather
            // than crash-loop, and the limiter's own fail-closed path covers the gap.
            config.AbortOnConnectFail = false;
            config.ConnectRetry = 3;
            config.ConnectTimeout = 5_000;

            var multiplexer = ConnectionMultiplexer.Connect(config);

            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("PdfWerk.Redis");
            multiplexer.ConnectionFailed += (_, e) => logger.LogError("Redis connection failed: {Failure}", e.FailureType);
            multiplexer.ConnectionRestored += (_, _) => logger.LogInformation("Redis connection restored.");

            return multiplexer;
        });

        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres");

        services.AddDbContextFactory<PdfWerkDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(postgres))
            {
                options.UseNpgsql(postgres);
                return;
            }

            // A file beside the application, so keys survive a restart during development.
            var sqlite = configuration.GetConnectionString("Sqlite");
            options.UseSqlite(string.IsNullOrWhiteSpace(sqlite) ? "Data Source=pdfwerk.db" : sqlite);
        });

        services.AddSingleton<IApiKeyStore, EfApiKeyStore>();
        services.AddSingleton<EfApiKeyStore>(sp => (EfApiKeyStore)sp.GetRequiredService<IApiKeyStore>());
    }

    /// <summary>
    /// Creates the schema if it is absent.
    /// </summary>
    /// <remarks>
    /// EnsureCreated is enough while the schema is new and there is no deployed data to migrate.
    /// Before the first release that changes a table, this should be swapped for EF migrations —
    /// EnsureCreated will not alter an existing schema, so a change would silently not apply.
    /// </remarks>
    public static async Task InitialiseStorageAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PdfWerk.Storage");

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Key store ready ({Provider}).", db.Database.ProviderName);
        }
        catch (Exception ex)
        {
            // API keys are optional: anonymous callers still work, so a storage failure must not
            // stop the service from starting.
            logger.LogError(ex, "Could not initialise the key store. API key issuance will not work.");
        }
    }
}
