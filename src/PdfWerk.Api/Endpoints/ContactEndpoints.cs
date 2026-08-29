using System.Text.RegularExpressions;
using PdfWerk.Api.Infrastructure;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// The contact form's endpoint.
/// </summary>
/// <remarks>
/// Routed through <see cref="ActionRunner"/> like every other operation, which is the point of
/// having made Contact an action: it inherits per-caller rate limiting, quota headers, request
/// logging and an editable ceiling in the admin portal. A public form that sends email through a
/// sender address we own is the most attractive thing here to abuse, and it would otherwise have
/// been the only unmetered endpoint on the service.
/// </remarks>
public static partial class ContactEndpoints
{
    /// <summary>Deliberately permissive. The point is to catch a typo, not to adjudicate RFC 5322.</summary>
    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();

    public static void MapContactEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/v1");

        // Lets the page say "unavailable, here is GitHub instead" before someone writes three
        // paragraphs and only then discovers they cannot be sent.
        v1.MapGet("/contact", (IContactSender sender) => Results.Ok(new { configured = sender.IsConfigured }))
          .WithSummary("Report whether the contact form can send on this instance.");

        v1.MapPost("/contact", (
                HttpContext context,
                ActionRunner runner,
                IContactSender sender,
                ContactForm form) =>
            runner.RunAsync(context, PdfWerkAction.Contact, async (limit, ct) =>
            {
                var name = (form.Name ?? string.Empty).Trim();
                var email = (form.Email ?? string.Empty).Trim();
                var message = (form.Message ?? string.Empty).Trim();

                // Answered as though it sent. A bot told it was caught simply retries with the
                // field left blank; one told "thank you" goes away satisfied.
                if (!string.IsNullOrWhiteSpace(form.Website))
                    return Results.Ok(new { sent = true });

                if (name.Length == 0) throw new PdfWerkException("Please tell us your name.");
                if (name.Length > 120) throw new PdfWerkException("That name is too long.");

                if (!EmailShape().IsMatch(email))
                    throw new PdfWerkException("That does not look like an email address we could reply to.");

                if (message.Length < 10)
                    throw new PdfWerkException("Please write a little more so we know what you need.");

                // Bounded by the tier's character ceiling, so the limit an administrator sets in
                // the portal is the one that applies here too.
                if (message.Length > limit.MaxCharacters)
                    throw new PdfWerkException($"Please keep the message under {limit.MaxCharacters:N0} characters.");

                await sender
                    .SendAsync(new ContactMessage { Name = name, Email = email, Message = message }, ct)
                    .ConfigureAwait(false);

                return Results.Ok(new { sent = true });
            }))
          .WithSummary("Send a message to whoever runs this instance.");
    }
}

public sealed record ContactForm(string? Name, string? Email, string? Message, string? Website);
