using PdfWerk.Api.Infrastructure;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Limits;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Word;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// The public API surface. Every action is one POST that takes a document (or the text to make
/// one) and returns a document, with the delivery mode chosen by the caller.
/// </summary>
public static class PdfEndpoints
{
    public static void MapPdfEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").WithTags("PdfWerk");

        MapCatalog(v1);
        MapCreate(v1);
        MapEdit(v1);
        MapForms(v1);
        MapCombine(v1);
    }

    // ---- discovery -------------------------------------------------------

    private static void MapCatalog(RouteGroupBuilder v1)
    {
        v1.MapGet("/actions", () => Results.Ok(ActionCatalog.All.Select(a => new
            {
                action = a.Action.ToString(),
                slug = a.Slug,
                title = a.Title,
                summary = a.Summary,
                requiresAi = a.RequiresAi,
                endpoint = $"/v1/{a.Slug}",
            })))
            .WithName("ListActions")
            .WithSummary("List every action the API exposes.");

        v1.MapGet("/quota", async (HttpContext context, IRateLimiter limiter, ClientResolver clients) =>
            {
                var client = clients.Resolve(context);

                var quotas = new List<object>();
                foreach (var descriptor in ActionCatalog.All)
                {
                    var remaining = await limiter.PeekAsync(client, descriptor.Action, context.RequestAborted)
                        .ConfigureAwait(false);

                    quotas.Add(new
                    {
                        action = descriptor.Action.ToString(),
                        remaining = remaining.ToDictionary(
                            r => r.Key,
                            r => r.Value == int.MaxValue ? "unlimited" : r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    });
                }

                return Results.Ok(new { tier = client.Tier.ToString(), quotas });
            })
            .WithName("GetQuota")
            .WithSummary("Report the caller's remaining quota for every action, without consuming any.");
    }

    // ---- creation --------------------------------------------------------

    private static void MapCreate(RouteGroupBuilder v1)
    {
        v1.MapPost("/create/text", (
                HttpContext context,
                CreateFromTextRequest request,
                ActionRunner runner,
                IPdfComposer composer) =>
                runner.RunAsync(context, PdfWerkAction.CreateFromText, (limit, _) =>
                {
                    RequestGuard.RequireTextBudget(request.Content, limit);

                    var artifact = composer.Create(request);
                    return Task.FromResult(ApiResults.Document(
                        artifact,
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object> { ["format"] = request.Format.ToString() }));
                }))
            .WithName("CreateFromText")
            .WithSummary("Render text or Markdown into a new PDF.");

        v1.MapPost("/create/word", (
                HttpContext context,
                IFormFile file,
                ActionRunner runner,
                WordConversionPipeline pipeline) =>
                runner.RunAsync(context, PdfWerkAction.CreateFromWord, async (limit, ct) =>
                {
                    var source = await RequestGuard.ReadAsync(file, limit, ct, "document").ConfigureAwait(false);
                    var result = await pipeline.ConvertAsync(source, file.FileName, ct).ConfigureAwait(false);

                    // The two converters differ in fidelity, so the caller is told which ran.
                    context.Response.Headers["X-PdfWerk-Converter"] = result.Converter;

                    return ApiResults.Document(
                        result.Artifact,
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object>
                        {
                            ["converter"] = result.Converter,
                            ["usedFallback"] = result.UsedFallback,
                        });
                }))
            .WithName("CreateFromWord")
            .WithSummary("Convert a .docx (or .doc, where LibreOffice is available) to PDF.")
            .DisableAntiforgery();
    }

    // ---- editing ---------------------------------------------------------

    private static void MapEdit(RouteGroupBuilder v1)
    {
        v1.MapPost("/edit/text", (
                HttpContext context,
                ActionRunner runner,
                IPdfTextEditor editor) =>
                runner.RunAsync(context, PdfWerkAction.EditText, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.RequireJsonPart<EditTextRequest>(form, "request");
                    RequestGuard.RequireBatchBudget(request.Replacements.Count, limit, "replacements");

                    var (artifact, count) = editor.ReplaceText(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, "edited") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object> { ["replacements"] = count });
                }))
            .WithName("EditText")
            .WithSummary("Find and replace text inside an existing PDF.")
            .DisableAntiforgery();

        v1.MapPost("/inspect", (
                HttpContext context,
                IFormFile file,
                ActionRunner runner,
                IPdfInspector inspector) =>
                runner.RunAsync(context, PdfWerkAction.Inspect, async (limit, ct) =>
                {
                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file.FileName);

                    return Results.Ok(inspector.Inspect(pdf, file.FileName));
                }))
            .WithName("Inspect")
            .WithSummary("Report page count, metadata, page sizes and the form field inventory.")
            .DisableAntiforgery();
    }

    // ---- forms -----------------------------------------------------------

    private static void MapForms(RouteGroupBuilder v1)
    {
        v1.MapPost("/forms/design", (
                HttpContext context,
                ActionRunner runner,
                IPdfFormService forms) =>
                runner.RunAsync(context, PdfWerkAction.EditFormFields, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.RequireJsonPart<EditFormFieldsRequest>(form, "request");
                    RequestGuard.RequireBatchBudget(request.Add.Count + request.Remove.Count, limit, "field changes");

                    var artifact = forms.EditFields(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, "form") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object>
                        {
                            ["added"] = request.Add.Count,
                            ["removed"] = request.Remove.Count,
                        });
                }))
            .WithName("DesignFormFields")
            .WithSummary("Add or remove AcroForm fields on an existing PDF.")
            .DisableAntiforgery();

        v1.MapPost("/forms/fill", (
                HttpContext context,
                ActionRunner runner,
                IPdfFormService forms) =>
                runner.RunAsync(context, PdfWerkAction.FillForm, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.RequireJsonPart<FillFormRequest>(form, "request");
                    RequestGuard.RequireBatchBudget(request.Values.Count, limit, "field values");

                    var artifact = forms.FillFields(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, request.Flatten ? "flattened" : "filled") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object>
                        {
                            ["fields"] = request.Values.Count,
                            ["flattened"] = request.Flatten,
                        });
                }))
            .WithName("FillForm")
            .WithSummary("Merge values into an existing form, optionally flattening it.")
            .DisableAntiforgery();
    }

    // ---- combining -------------------------------------------------------

    private static void MapCombine(RouteGroupBuilder v1)
    {
        v1.MapPost("/merge", (
                HttpContext context,
                ActionRunner runner,
                IPdfMerger merger) =>
                runner.RunAsync(context, PdfWerkAction.Merge, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);

                    var files = form.Files.Where(f => f.Length > 0).ToList();
                    if (files.Count < 2)
                        throw new PdfWerkException("Upload at least two PDFs to merge.");

                    RequestGuard.RequireBatchBudget(files.Count, limit, "files");

                    var documents = new List<(string FileName, byte[] Content)>(files.Count);
                    var totalPages = 0;

                    foreach (var file in files)
                    {
                        var content = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);

                        // Guard the combined length, not just each file: twenty short documents
                        // can still exceed the page budget the tier is allowed.
                        totalPages += PdfWerk.Pdf.PdfProbe.PageCount(content);
                        if (totalPages > limit.MaxPages)
                        {
                            throw new LimitExceededException(
                                $"The merged document would have {totalPages} pages; the limit for your tier is {limit.MaxPages}.");
                        }

                        documents.Add((file.FileName, content));
                    }

                    var outputName = form.TryGetValue("fileName", out var requested) && !string.IsNullOrWhiteSpace(requested)
                        ? requested.ToString()
                        : "merged.pdf";

                    var artifact = merger.Merge(documents, outputName);

                    return ApiResults.Document(
                        artifact,
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object> { ["files"] = documents.Count, ["pages"] = totalPages });
                }))
            .WithName("Merge")
            .WithSummary("Combine several PDFs into one, in the order supplied.")
            .DisableAntiforgery();
    }

    private static string Rename(string original, string suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        return string.IsNullOrWhiteSpace(stem) ? $"{suffix}.pdf" : $"{stem}-{suffix}.pdf";
    }
}
