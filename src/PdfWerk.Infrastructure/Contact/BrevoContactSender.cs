using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Infrastructure.Contact;

public sealed class ContactOptions
{
    public const string SectionName = "Contact";

    /// <summary>Brevo transactional API key. Empty disables the contact form entirely.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Where messages are delivered.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// The sending address. Must be a sender Brevo has verified for your account, or every send
    /// is refused — this is the one setting people get wrong, because it looks like it should be
    /// free text.
    /// </summary>
    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "PdfWerk";

    /// <summary>Shown in the subject line so messages from several instances stay apart.</summary>
    public string SiteName { get; set; } = "pdfwerk.com";
}

/// <summary>
/// Delivers contact messages through Brevo's transactional API.
/// </summary>
/// <remarks>
/// The JSON API rather than SMTP, which is what makes the sender's name and address safe to pass
/// through: they travel as JSON values, so a newline in the visitor's name cannot become an extra
/// mail header. Building the same message by concatenating SMTP headers is how contact forms turn
/// into open relays.
///
/// The visitor's address goes in Reply-To and never in From. Sending as the visitor would mean
/// sending from a domain we are not authorised for, which fails SPF and teaches the receiving
/// server that our real mail is worth distrusting too.
/// </remarks>
public sealed class BrevoContactSender(
    HttpClient http,
    IOptions<ContactOptions> options,
    ILogger<BrevoContactSender> logger) : IContactSender
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly ContactOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.To) &&
        !string.IsNullOrWhiteSpace(_options.From);

    public async Task SendAsync(ContactMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new PdfWerkException(
                "The contact form is not configured on this instance. Open an issue on GitHub instead.",
                503);
        }

        var payload = new BrevoEmail
        {
            Sender = new BrevoAddress { Email = _options.From, Name = _options.FromName },
            To = [new BrevoAddress { Email = _options.To }],
            ReplyTo = new BrevoAddress { Email = message.Email, Name = message.Name },
            Subject = $"Contact form — {_options.SiteName}",
            TextContent = Compose(message),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Logged with the status because Brevo's refusals are specific and worth reading: an
        // unverified sender and an exhausted quota look identical from the browser otherwise.
        logger.LogError("Brevo refused the message: {Status} {Body}", (int)response.StatusCode, body);

        throw new PdfWerkException(
            response.StatusCode == HttpStatusCode.Unauthorized
                ? "The contact form is misconfigured on this instance. Open an issue on GitHub instead."
                : "That message could not be sent just now. Please try again shortly.",
            502);
    }

    /// <summary>
    /// Plain text rather than HTML.
    /// </summary>
    /// <remarks>
    /// There is nothing to format, and a plain body cannot carry markup from a stranger into an
    /// inbox — no escaping to get right, and nothing for a mail client to render.
    /// </remarks>
    private static string Compose(ContactMessage message) =>
        $"""
         From: {message.Name} <{message.Email}>

         {message.Message}
         """;

    private sealed record BrevoAddress
    {
        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }

    private sealed record BrevoEmail
    {
        [JsonPropertyName("sender")]
        public required BrevoAddress Sender { get; init; }

        [JsonPropertyName("to")]
        public required IReadOnlyList<BrevoAddress> To { get; init; }

        [JsonPropertyName("replyTo")]
        public BrevoAddress? ReplyTo { get; init; }

        [JsonPropertyName("subject")]
        public required string Subject { get; init; }

        [JsonPropertyName("textContent")]
        public required string TextContent { get; init; }
    }
}
