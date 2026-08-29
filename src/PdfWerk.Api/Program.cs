using System.Text.Json.Serialization;
using PdfWerk.Ai;
using PdfWerk.Api.Endpoints;
using PdfWerk.Api.Infrastructure;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Infrastructure;
using PdfWerk.Infrastructure.RateLimiting;
using PdfWerk.Pdf;
using PdfWerk.Pdf.Word;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- configuration ------------------------------------------------------

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
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

var app = builder.Build();

// ---- pipeline -----------------------------------------------------------

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference("/docs");

app.MapPdfEndpoints();
app.MapPageEndpoints();
app.MapKeyEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "pdfwerk" }))
   .ExcludeFromDescription();

// The UI is a single-page app, so a deep link like /forms has no file behind it. Anything that
// is not an API route or a real asset is handed to index.html for the router to resolve.
app.MapFallbackToFile("index.html");

await InfrastructureServiceCollectionExtensions.InitialiseStorageAsync(app.Services);

WarnIfLimiterIsSingleProcess(app);

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

/// <summary>Exposed so integration tests can spin the host up in-process.</summary>
public partial class Program;
