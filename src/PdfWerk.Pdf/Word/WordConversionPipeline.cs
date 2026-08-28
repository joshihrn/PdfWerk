using Microsoft.Extensions.Logging;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;

namespace PdfWerk.Pdf.Word;

/// <summary>The document produced, and which converter produced it.</summary>
public sealed record WordConversionResult(PdfArtifact Artifact, string Converter, bool UsedFallback);

/// <summary>
/// Picks a Word converter at request time and falls back when the preferred one cannot run.
/// </summary>
/// <remarks>
/// Availability is decided per request rather than at startup so that a LibreOffice install
/// appearing — or a sidecar container becoming reachable — does not require a restart. The
/// converter that ran is reported back to the caller and surfaced as a response header, because
/// the two paths differ in fidelity and a user chasing a layout problem needs to know which one
/// produced their file.
/// </remarks>
public sealed class WordConversionPipeline(
    IEnumerable<IWordConverter> converters,
    ILogger<WordConversionPipeline> logger)
{
    private readonly IReadOnlyList<IWordConverter> _converters =
        converters.OrderBy(c => c.Priority).ToList();

    public async Task<WordConversionResult> ConvertAsync(byte[] source, string fileName, CancellationToken ct = default)
    {
        if (source.Length == 0)
            throw new PdfWerkException("The uploaded file was empty.");

        if (_converters.Count == 0)
            throw new PdfWerkException("No Word converter is configured on this server.");

        var attempted = new List<string>();
        PdfWerkException? firstFailure = null;

        for (var i = 0; i < _converters.Count; i++)
        {
            var converter = _converters[i];

            if (!await converter.IsAvailableAsync(ct).ConfigureAwait(false))
                continue;

            attempted.Add(converter.Name);

            try
            {
                var artifact = await converter.ConvertAsync(source, fileName, ct).ConfigureAwait(false);

                return new WordConversionResult(artifact, converter.Name, UsedFallback: attempted.Count > 1);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PdfWerkException ex)
            {
                // The caller's fault — a corrupt or protected file — will fail identically on
                // every converter, so there is nothing to gain by trying the next one.
                firstFailure ??= ex;
                logger.LogWarning(ex, "Converter {Converter} rejected {File}.", converter.Name, fileName);
            }
            catch (Exception ex)
            {
                // An environment failure, though: the next converter may well succeed.
                logger.LogError(ex, "Converter {Converter} failed unexpectedly on {File}.", converter.Name, fileName);
            }
        }

        if (firstFailure is not null)
            throw firstFailure;

        throw new PdfWerkException(
            attempted.Count == 0
                ? "No Word converter is available on this server."
                : $"This document could not be converted (tried: {string.Join(", ", attempted)}).");
    }
}
