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

        // Singletons because each holds a cached snapshot consulted on every request: the block
        // list, the effective limits, and the buffered log writer.
        services.AddSingleton<IRequestLog, EfRequestLog>();
        services.AddSingleton<IIpBlockList, EfIpBlockList>();
        services.AddSingleton<IRateLimitSettings, EfRateLimitSettings>();
    }

    /// <summary>
    /// Creates the schema if it is absent.
    /// </summary>
    /// <remarks>
    /// Migrations rather than EnsureCreated, for Postgres. The admin tables were the first schema
    /// change after release-shaped data existed, and EnsureCreated will not alter an existing
    /// database — it would have left the new tables and the IsAdmin column silently absent.
    ///
    /// A migration carries the type names of the provider it was scaffolded against, so one set
    /// cannot serve both providers. These are Postgres migrations, because Postgres is what gets
    /// deployed; SQLite is the fallback for running without any configuration at all, where the
    /// database is a scratch file, so it gets the schema straight from the model instead. That is
    /// already what the tests do.
    ///
    /// The cost is real and worth naming: on SQLite a model change does not migrate an existing
    /// file, it is simply absent from it. Hence the warning below rather than silence.
    /// </remarks>
    public static async Task InitialiseStorageAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<PdfWerkDbContext>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("PdfWerk.Storage");

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

            if (db.Database.IsNpgsql())
            {
                await db.Database.MigrateAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

                logger.LogWarning(
                    "Storage is {Provider}, so the schema comes from the model and is never " +
                    "migrated. Set ConnectionStrings:Postgres for anything you intend to keep.",
                    db.Database.ProviderName);
            }

            logger.LogInformation("Storage ready ({Provider}).", db.Database.ProviderName);

            // The snapshots these hold are read on every request, so they have to be populated
            // before the first one arrives rather than lazily on first miss.
            await services.GetRequiredService<IIpBlockList>().RefreshAsync(ct).ConfigureAwait(false);
            await services.GetRequiredService<IRateLimitSettings>().RefreshAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // API keys are optional: anonymous callers still work, so a storage failure must not
            // stop the service from starting.
            logger.LogError(ex, "Could not initialise the key store. API key issuance will not work.");
        }
    }
}
