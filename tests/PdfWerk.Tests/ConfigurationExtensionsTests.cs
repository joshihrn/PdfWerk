using Microsoft.Extensions.Configuration;
using PdfWerk.Infrastructure;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// A declared-but-blank setting must not shadow a real one behind it.
/// </summary>
/// <remarks>
/// Optional settings are declared in appsettings.json as "" so they are discoverable. An empty
/// string is not null, so the obvious `a ?? b` stops at the declaration — which is how the
/// Application Insights connection string came to be ignored on a deployment that had it set
/// correctly, with nothing failing and nothing logged.
/// </remarks>
public class ConfigurationExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void An_empty_declaration_falls_through_to_the_next_key()
    {
        var configuration = Config(
            ("ApplicationInsights:ConnectionString", ""),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=abc"));

        Assert.Equal(
            "InstrumentationKey=abc",
            configuration.FirstConfigured("ApplicationInsights:ConnectionString", "APPLICATIONINSIGHTS_CONNECTION_STRING"));
    }

    [Fact]
    public void Whitespace_counts_as_absent()
    {
        var configuration = Config(("a", "   "), ("b", "real"));

        Assert.Equal("real", configuration.FirstConfigured("a", "b"));
    }

    [Fact]
    public void The_first_real_value_wins()
    {
        var configuration = Config(("a", "first"), ("b", "second"));

        Assert.Equal("first", configuration.FirstConfigured("a", "b"));
    }

    [Fact]
    public void All_blank_returns_null_so_the_caller_can_switch_the_feature_off()
    {
        var configuration = Config(("a", ""), ("b", null));

        Assert.Null(configuration.FirstConfigured("a", "b", "missing-entirely"));
    }
}
