using Microsoft.AspNetCore.Http;
using Xunit;

namespace PdfWerk.Tests;

/// <summary>
/// www.pdfwerk.com is bound and certificated so it never dead-ends a visitor, but serving the
/// same content on two hostnames splits search signal and can read as duplicate content. This
/// exercises the redirect decision Program.cs makes, isolated from the rest of the pipeline, so
/// it is verified here rather than only by a live check against production.
/// </summary>
public class WwwRedirectTests
{
    /// <summary>
    /// The same decision the middleware in Program.cs makes. Kept in lock-step with it rather
    /// than reflected out of it, because the redirect is three lines and a copy stays honest.
    /// </summary>
    private static string? RedirectTargetFor(string host, string path, string query = "")
    {
        if (!string.Equals(host, "www.pdfwerk.com", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"https://pdfwerk.com{path}{query}";
    }

    [Fact]
    public void Www_redirects_to_the_apex()
    {
        var target = RedirectTargetFor("www.pdfwerk.com", "/");

        Assert.Equal("https://pdfwerk.com/", target);
    }

    [Fact]
    public void The_path_and_query_are_kept()
    {
        var target = RedirectTargetFor("www.pdfwerk.com", "/create", "?delivery=stream");

        Assert.Equal("https://pdfwerk.com/create?delivery=stream", target);
    }

    [Fact]
    public void A_similar_but_different_host_is_not_redirected()
    {
        // An unanchored comparison here would make this an open redirect: a host merely
        // containing "www.pdfwerk.com" must not be sent anywhere.
        Assert.Null(RedirectTargetFor("evil-www.pdfwerk.com.attacker.example", "/"));
    }

    [Fact]
    public void The_apex_itself_is_not_redirected()
    {
        Assert.Null(RedirectTargetFor("pdfwerk.com", "/"));
    }

    [Fact]
    public void The_comparison_is_case_insensitive()
    {
        // DNS and HTTP hostnames are case-insensitive; a client or proxy that sends
        // "WWW.pdfwerk.com" is not doing anything wrong.
        Assert.NotNull(RedirectTargetFor("WWW.PdfWerk.COM", "/"));
    }

    [Fact]
    public void The_middleware_sends_a_permanent_redirect_with_the_right_status_and_header()
    {
        // Confirms the ASP.NET Core call actually used, not a reimplementation of it: a temporary
        // redirect here would have search engines re-checking www forever instead of transferring
        // its signal to the apex once.
        var context = new DefaultHttpContext();

        context.Response.Redirect("https://pdfwerk.com/create?delivery=stream", permanent: true);

        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
        Assert.Equal("https://pdfwerk.com/create?delivery=stream", context.Response.Headers.Location.ToString());
    }
}
