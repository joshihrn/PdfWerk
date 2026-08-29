namespace PdfWerk.Core.Abstractions;

/// <summary>A message from the contact form.</summary>
public sealed record ContactMessage
{
    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// A field no human ever fills in, because it is hidden.
    /// </summary>
    /// <remarks>
    /// Most form spam comes from scripts that fill every input they find rather than from anything
    /// that renders the page. A honeypot catches those for the price of one hidden field, and
    /// unlike a CAPTCHA it costs a real visitor nothing — no puzzle, no third-party script, no
    /// accessibility problem.
    /// </remarks>
    public string? Website { get; init; }
}

/// <summary>Sends contact messages on to whoever runs this instance.</summary>
public interface IContactSender
{
    /// <summary>False when no mail provider is configured, which is the normal self-hosted state.</summary>
    bool IsConfigured { get; }

    Task SendAsync(ContactMessage message, CancellationToken ct = default);
}
