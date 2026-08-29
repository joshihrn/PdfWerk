using System.IO.Compression;
using PdfWerk.Api.Infrastructure;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;

namespace PdfWerk.Api.Endpoints;

/// <summary>
/// Page-level operations: split, rotate and watermark.
/// </summary>
public static class PageEndpoints
{
    public static void MapPageEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").WithTags("PdfWerk");

        MapSplit(v1);
        MapRotate(v1);
        MapWatermark(v1);
        MapProtect(v1);
    }

    private static void MapProtect(RouteGroupBuilder v1)
    {
        v1.MapPost("/protect", (
                HttpContext context,
                ActionRunner runner,
                IPdfProtector protector) =>
                runner.RunAsync(context, PdfWerkAction.Protect, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.RequireJsonPart<ProtectRequest>(form, "request");
                    var artifact = protector.Protect(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, "protected") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object>
                        {
                            ["encrypted"] = !string.IsNullOrEmpty(request.UserPassword),

                            // Said plainly, because the difference matters: a user password
                            // encrypts, permission flags only ask readers to behave.
                            ["note"] = string.IsNullOrEmpty(request.UserPassword)
                                ? "No user password was set, so the document opens without one. Permission flags rely on the reader honouring them and are not a security control."
                                : "The document now requires the user password to open. Permission flags rely on the reader honouring them and are not a security control.",
                        });
                }))
            .WithName("Protect")
            .WithSummary("Set a password to open the document, and restrict printing, copying or editing.")
            .DisableAntiforgery();
    }

    private static void MapSplit(RouteGroupBuilder v1)
    {
        v1.MapPost("/split", (
                HttpContext context,
                ActionRunner runner,
                IPdfSplitter splitter) =>
                runner.RunAsync(context, PdfWerkAction.Split, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.ReadJsonPart(form, "request", new SplitRequest());
                    var parts = splitter.Split(pdf, request, file.FileName);

                    // Bursting a long document can produce hundreds of files, which is a cheap
                    // way to turn one request into a lot of work and a very large response.
                    RequestGuard.RequireBatchBudget(parts.Count, limit, "output documents");

                    context.Response.Headers["X-PdfWerk-Parts"] = parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    // A single output is returned as-is; several are bundled, because a caller
                    // cannot receive multiple documents in one HTTP response otherwise.
                    if (parts.Count == 1)
                    {
                        return ApiResults.Document(
                            new PdfArtifact(parts[0].Content, parts[0].Name),
                            ApiResults.DeliveryFrom(context.Request),
                            new Dictionary<string, object> { ["parts"] = 1, ["pages"] = parts[0].Pages });
                    }

                    var archive = BuildZip(parts);
                    var name = Path.GetFileNameWithoutExtension(file.FileName) + "-split.zip";

                    return ApiResults.Document(
                        new PdfArtifact(archive, name, "application/zip"),
                        // A zip cannot be previewed inline, so streaming it would just confuse.
                        DeliveryMode.Download,
                        new Dictionary<string, object> { ["parts"] = parts.Count });
                }))
            .WithName("Split")
            .WithSummary("Extract page ranges, burst into single pages, or split into groups. Several outputs are returned as a zip.")
            .DisableAntiforgery();
    }

    private static void MapRotate(RouteGroupBuilder v1)
    {
        v1.MapPost("/rotate", (
                HttpContext context,
                ActionRunner runner,
                IPdfRotator rotator) =>
                runner.RunAsync(context, PdfWerkAction.Rotate, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.ReadJsonPart(form, "request", new RotateRequest());
                    var artifact = rotator.Rotate(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, "rotated") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object> { ["degrees"] = request.Degrees });
                }))
            .WithName("Rotate")
            .WithSummary("Rotate selected pages by 90, 180 or 270 degrees.")
            .DisableAntiforgery();
    }

    private static void MapWatermark(RouteGroupBuilder v1)
    {
        v1.MapPost("/watermark", (
                HttpContext context,
                ActionRunner runner,
                IPdfWatermarker watermarker) =>
                runner.RunAsync(context, PdfWerkAction.Watermark, async (limit, ct) =>
                {
                    var form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
                    var file = form.Files["file"];

                    var pdf = await RequestGuard.ReadAsync(file, limit, ct).ConfigureAwait(false);
                    RequestGuard.RequirePageBudget(pdf, limit, file!.FileName);

                    var request = RequestGuard.RequireJsonPart<WatermarkRequest>(form, "request");
                    var artifact = watermarker.Apply(pdf, request);

                    return ApiResults.Document(
                        artifact with { FileName = Rename(file.FileName, "watermarked") },
                        ApiResults.DeliveryFrom(context.Request),
                        new Dictionary<string, object> { ["text"] = request.Text });
                }))
            .WithName("Watermark")
            .WithSummary("Stamp text across selected pages, over or beneath the content.")
            .DisableAntiforgery();
    }

    private static byte[] BuildZip(IReadOnlyList<SplitPart> parts)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var part in parts)
            {
                // PDFs are already compressed, so storing them avoids spending CPU for nothing.
                var entry = archive.CreateEntry(part.Name, CompressionLevel.NoCompression);

                using var stream = entry.Open();
                stream.Write(part.Content, 0, part.Content.Length);
            }
        }

        return buffer.ToArray();
    }

    private static string Rename(string original, string suffix)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        return string.IsNullOrWhiteSpace(stem) ? $"{suffix}.pdf" : $"{stem}-{suffix}.pdf";
    }
}
