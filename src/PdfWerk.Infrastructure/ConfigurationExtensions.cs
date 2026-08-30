using Microsoft.Extensions.Configuration;

namespace PdfWerk.Infrastructure;

/// <summary>Reading settings that may be declared but left blank.</summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// The first key that holds an actual value, ignoring ones that are absent or blank.
    /// </summary>
    /// <remarks>
    /// <para>
    /// appsettings.json declares optional settings as <c>""</c> so they are discoverable — someone
    /// reading the file can see the key exists and what it is called. An empty string is not null,
    /// so <c>a ?? b</c> stops at the declaration and never reaches the environment variable that
    /// actually holds the value.
    /// </para>
    /// <para>
    /// That is not hypothetical: it is exactly how the Application Insights connection string was
    /// read, and telemetry was silently disabled on a deployment where it was correctly
    /// configured. Nothing failed, nothing logged, and the absence of data looked like an absence
    /// of traffic.
    /// </para>
    /// </remarks>
    public static string? FirstConfigured(this IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
