namespace PdfWerk.Core.Abstractions;

/// <summary>
/// Turns a short brief into the Markdown body of a document.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IPdfComposer"/>. Composing is deterministic, offline and
/// fast; drafting calls a remote model, can fail, and costs money. Folding one into the other
/// would make every plain render depend on a provider being reachable.
/// </remarks>
public interface IDocumentDrafter
{
    /// <summary>
    /// Drafts a document body from a brief.
    /// </summary>
    /// <param name="brief">What the document should say, in the caller's own words.</param>
    /// <param name="title">Optional title, used to steer the draft rather than printed by this.</param>
    /// <param name="provider">Provider key, or null for the configured default.</param>
    /// <exception cref="AiUnavailableException">No provider is configured.</exception>
    Task<DraftedDocument> DraftAsync(
        string brief,
        string? title = null,
        string? provider = null,
        CancellationToken ct = default);
}

/// <summary>A drafted body, with the model that produced it so the caller can say.</summary>
public sealed record DraftedDocument
{
    /// <summary>Markdown, ready to hand to the composer.</summary>
    public required string Content { get; init; }

    public required string Model { get; init; }

    public required string Provider { get; init; }
}
