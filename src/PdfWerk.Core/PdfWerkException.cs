namespace PdfWerk.Core;

/// <summary>
/// A failure that is the caller's fault and is safe to describe back to them.
/// Anything else bubbles up as a 500 with the detail suppressed.
/// </summary>
public class PdfWerkException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>The supplied file was not a readable PDF, or was encrypted with a password we don't have.</summary>
public sealed class InvalidPdfException(string message) : PdfWerkException(message, 422);

/// <summary>The request exceeded a hard safety limit (size, pages, fields, batch count).</summary>
public sealed class LimitExceededException(string message) : PdfWerkException(message, 413);

/// <summary>No AI provider is configured, or the upstream provider refused the request.</summary>
public sealed class AiUnavailableException(string message) : PdfWerkException(message, 503);
