using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using PdfWerk.Ai;
using PdfWerk.Api.Endpoints;
using PdfWerk.Api.Infrastructure;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Infrastructure;
using PdfWerk.Infrastructure.Contact;
using PdfWerk.Infrastructure.RateLimiting;
using PdfWerk.Pdf;
using PdfWerk.Pdf.Word;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- telemetry ----------------------------------------------------------

/*
 * Registered before anything else so a failure during start-up is still reported.
 *
 * The connection string comes from configuration and is absent by default, which turns the whole
 * thing off — a self-hosted copy should not be made to send telemetry anywhere, and the local
 * suite should not need a resource to run against.
 *
 * This exists because of a real outage: EF refused a migration, the failure was caught and logged
 * so anonymous callers would keep working, and the site went on answering /health with 200 while
 * every write returned 500. Nothing surfaced it. Finding it meant pulling container logs out of
 * Kudu by hand. ILogger output flows here too, so that same swallowed error is now a searchable
 * exception rather than a line in a file nobody reads.
 */
// FirstConfigured, not ??. appsettings.json declares the key as "" so it is discoverable, and an
// empty string is not null — so ?? stopped at the declaration and never reached the environment
// variable App Service sets, which disabled telemetry on a correctly configured deployment
// without a word being logged about it.
var telemetryConnection = builder.Configuration.FirstConfigured(
    "ApplicationInsights:ConnectionString",
    "APPLICATIONINSIGHTS_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(telemetryConnection))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
        options.ConnectionString = telemetryConnection);
}

// ---- configuration ------------------------------------------------------

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<ContactOptions>(builder.Configuration.GetSection(ContactOptions.SectionName));

// A typed client so the send has a timeout of its own: a contact form that hangs because a mail
// provider is slow holds a request open and, worse, holds one of the caller's concurrency slots.
builder.Services.AddHttpClient<IContactSender, BrevoContactSender>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));

// Only registered when a retention window is set. Indefinite is the default, and a background
// loop that exists solely to decide it has nothing to do is noise.
var retentionDays = builder.Configuration.GetValue<int>($"{AdminOptions.SectionName}:RetentionDays");

if (retentionDays > 0)
{
    builder.Services.AddHostedService(provider => new RequestLogPruner(
        provider.GetRequiredService<IRequestLog>(),
        retentionDays,
        provider.GetRequiredService<ILogger<RequestLogPruner>>()));
}
builder.Services.Configure<ClientOptions>(builder.Configuration.GetSection(ClientOptions.SectionName));
builder.Services.Configure<LibreOfficeOptions>(builder.Configuration.GetSection(LibreOfficeOptions.SectionName));

// Enums travel as their names, so an API consumer writes "Markdown" rather than 1.
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ---- engine -------------------------------------------------------------

builder.Services.AddSingleton<IPdfComposer, PdfComposer>();
builder.Services.AddSingleton<IPdfFormService, PdfFormService>();
builder.Services.AddSingleton<IPdfTextEditor, PdfTextEditor>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<IPdfMerger, PdfMerger>();
builder.Services.AddSingleton<IPdfInspector, PdfInspector>();
builder.Services.AddSingleton<IPdfSplitter, PdfSplitter>();
builder.Services.AddSingleton<IPdfRotator, PdfRotator>();
builder.Services.AddSingleton<IPdfWatermarker, PdfWatermarker>();
builder.Services.AddSingleton<IPdfProtector, PdfProtector>();

builder.Services.AddSingleton<IWordConverter, LibreOfficeWordConverter>();
builder.Services.AddSingleton<IWordConverter, OpenXmlWordConverter>();
builder.Services.AddSingleton<WordConversionPipeline>();

builder.Services.AddPdfWerkAi(builder.Configuration);

// ---- request plumbing ---------------------------------------------------

// Picks Redis + Postgres when their connection strings are present, and single-process
// fallbacks otherwise, so the same binary runs locally and in production.
builder.Services.AddPdfWerkInfrastructure(builder.Configuration);

builder.Services.AddSingleton<ClientResolver>();
builder.Services.AddSingleton<ActionRunner>();

// The plugin is embedded on third-party pages, so the API must be reachable cross-origin.
// Credentials are never used, which is what makes a permissive origin list safe here.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(
        "Content-Disposition",
        "X-PdfWerk-Action",
        "X-PdfWerk-Converter",
        "X-RateLimit-Limit",
        "X-RateLimit-Remaining",
        "X-RateLimit-Reset",
        "X-RateLimit-Window")));

builder.Services.AddOpenApi(openApi => openApi.AddDocumentTransformer((document, _, _) =>
{
    // Without this the document is titled after the assembly, which is not what a public API
    // reference should advertise.
    document.Info.Title = "PdfWerk API";
    document.Info.Version = "v1";
    document.Info.Description =
        "Create, edit, merge, fill and summarise PDFs over HTTP. Every endpoint that returns a " +
        "document accepts `?delivery=download|stream|json`. Rate limits apply per action and are " +
        "reported on every response in the X-RateLimit-* headers.";

    document.Info.License = new Microsoft.OpenApi.Models.OpenApiLicense
    {
        Name = "Business Source License 1.1",
        Url = new Uri("https://github.com/joshihrn/PdfWerk/blob/main/LICENSE"),
    };

    return Task.CompletedTask;
}));

// Uploads are bounded per tier inside the handlers; this is the outer backstop that stops a
// hostile body being buffered at all.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(form =>
{
    form.MultipartBodyLengthLimit = 128L * 1024 * 1024;
    form.ValueLengthLimit = 8 * 1024 * 1024;      // the JSON options part
    form.MultipartHeadersLengthLimit = 32 * 1024;
});

// Page metadata, shared with the web app through the file the build copies into wwwroot. Absent
// when the API runs without the UI built alongside it, in which case the SEO routes stay off.
builder.Services.AddSingleton(provider =>
{
    var environment = provider.GetRequiredService<IWebHostEnvironment>();
    var configuration = provider.GetRequiredService<IConfiguration>();

    var baseUrl = configuration["Seo:BaseUrl"] ?? "https://pdfwerk.com";
    var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    return SeoCatalogue.Load(root, baseUrl)!;
});

builder.Services.AddSingleton(provider =>
{
    var environment = provider.GetRequiredService<IWebHostEnvironment>();
    var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

    var configuration = provider.GetRequiredService<IConfiguration>();

    return new SeoShell(
        provider.GetRequiredService<SeoCatalogue>(),
        Path.Combine(root, "index.html"),
        configuration["Analytics:MeasurementId"] ?? string.Empty);
});

var app = builder.Build();

// ---- pipeline -----------------------------------------------------------

app.UseCors();

// Ahead of everything else: a blocked caller should not reach the rate limiter, the key store or
// the PDF engine, and the log should record what they attempted regardless of how it ended.
app.UseMiddleware<RequestAuditMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference("/docs");

app.MapAdminEndpoints();
app.MapContactEndpoints();
app.MapPdfEndpoints();
app.MapPageEndpoints();
app.MapKeyEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "pdfwerk" }))
   .ExcludeFromDescription();

// The UI is a single-page app, so a deep link like /forms has no file behind it. Anything that
// is not an API route or a real asset is handed the shell for the router to resolve — with that
// page's own metadata written into it, since crawlers and link previews do not run the app.
app.MapSeoEndpoints();

await InfrastructureServiceCollectionExtensions.InitialiseStorageAsync(app.Services);
await BootstrapAdminAsync(app);

WarnIfLimiterIsSingleProcess(app);

// Stated explicitly, because "no telemetry" and "no traffic" look identical from the other end.
app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("PdfWerk.Telemetry")
    .LogInformation(
        string.IsNullOrWhiteSpace(telemetryConnection)
            ? "Application Insights is off: no connection string is configured."
            : "Application Insights is on.");

app.Run();

/// <summary>
/// The in-memory limiter counts per process, so behind more than one instance the effective
/// quota multiplies. That is fine locally and fatal for a public deployment, so it is called out
/// loudly rather than left to be discovered.
/// </summary>
static void WarnIfLimiterIsSingleProcess(WebApplication app)
{
    if (app.Environment.IsDevelopment())
        return;

    if (app.Services.GetRequiredService<IRateLimiter>() is not InMemoryRateLimiter)
        return;

    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("PdfWerk.Startup")
        .LogWarning(
            "Rate limiting is running in memory. Quotas are enforced per process, so running " +
            "more than one instance multiplies every limit. Set ConnectionStrings:Redis before " +
            "exposing this publicly.");
}

/// <summary>
/// Mints the first administrator's key from configuration, if one is set and none exists yet.
/// </summary>
/// <remarks>
/// The chicken-and-egg problem: the admin API is the only way to grant admin rights, and it can
/// only be reached with a key that already has them. So the first one comes from the host, where
/// whoever runs the service already has full control anyway.
///
/// It runs once. With a key already in place the setting is ignored, so leaving it in a config
/// file does not keep re-creating a credential — but it should still be removed, because a
/// bootstrap secret left in configuration is a standing back door.
/// </remarks>
static async Task BootstrapAdminAsync(WebApplication app)
{
    var options = app.Services.GetRequiredService<IOptions<AdminOptions>>().Value;
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PdfWerk.Admin");

    if (string.IsNullOrWhiteSpace(options.BootstrapKey))
    {
        var store = app.Services.GetRequiredService<IApiKeyStore>();

        if (!await store.AnyAdminAsync().ConfigureAwait(false))
        {
            logger.LogInformation(
                "No administrator key exists. Set Admin:BootstrapKey to a secret beginning 'pw_' " +
                "and at least 24 characters long, then restart to create one.");
        }

        return;
    }

    try
    {
        var keys = app.Services.GetRequiredService<IApiKeyStore>();

        // Keyed on whether this particular secret already works, not on whether any admin exists.
        //
        // "I set this key, so this key should work" is what an operator means, and it is also the
        // way back in when the only admin key has been lost: set it, restart, sign in. Checking
        // only for the existence of some admin would leave them locked out of their own service
        // with the setting apparently applied.
        if (await keys.ValidateAsync(options.BootstrapKey).ConfigureAwait(false) is { IsAdmin: true })
            return;

        await keys.CreateAdminAsync("bootstrap administrator", options.BootstrapKey).ConfigureAwait(false);

        // The secret itself is never logged: it came from configuration, so whoever needs it has
        // it already, and an audit log is a poor place to keep a credential.
        logger.LogWarning(
            "Created the first administrator key from configuration. Remove Admin:BootstrapKey now " +
            "that it exists — a bootstrap secret left in place is a standing back door.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not create the bootstrap administrator key.");
    }
}

/// <summary>Exposed so integration tests can spin the host up in-process.</summary>
public partial class Program;
