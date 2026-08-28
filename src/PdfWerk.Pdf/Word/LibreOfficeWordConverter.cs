using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PdfWerk.Core;
using PdfWerk.Core.Abstractions;
using PdfWerk.Core.Models;
using PdfWerk.Pdf.Internal;

namespace PdfWerk.Pdf.Word;

public sealed class LibreOfficeOptions
{
    public const string SectionName = "LibreOffice";

    /// <summary>Explicit path to the soffice binary. Empty means probe the usual locations.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Hard ceiling on one conversion. LibreOffice can wedge on a malformed file.</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Set false to force the managed renderer even where LibreOffice is installed.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Converts word-processor documents by driving a headless LibreOffice.
/// </summary>
/// <remarks>
/// <para>
/// This is the high-fidelity path: LibreOffice implements the whole Word layout model, so
/// complex documents — floating images, section breaks, styles, footnotes — come out looking
/// like the original. It is preferred whenever the binary is present.
/// </para>
/// <para>
/// LibreOffice is invoked as a separate process, so its MPL-2.0 licence does not reach into
/// this codebase. Each conversion gets a private user profile directory: without one, concurrent
/// invocations contend over the same profile lock and fail intermittently under load.
/// </para>
/// </remarks>
public sealed class LibreOfficeWordConverter(
    IOptions<LibreOfficeOptions> options,
    ILogger<LibreOfficeWordConverter> logger) : IWordConverter
{
    private readonly LibreOfficeOptions _options = options.Value;
    private string? _resolvedPath;
    private bool _probed;

    public string Name => "libreoffice";

    public int Priority => 0;

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return ValueTask.FromResult(false);

        // Probed once: hitting the filesystem on every request would be wasteful, and an
        // install appearing mid-process is not a case worth handling.
        if (!_probed)
        {
            _resolvedPath = Locate();
            _probed = true;

            if (_resolvedPath is null)
                logger.LogInformation("LibreOffice was not found; Word conversion will use the managed renderer.");
            else
                logger.LogInformation("LibreOffice found at {Path}.", _resolvedPath);
        }

        return ValueTask.FromResult(_resolvedPath is not null);
    }

    public async Task<PdfArtifact> ConvertAsync(byte[] source, string fileName, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync(ct).ConfigureAwait(false) || _resolvedPath is null)
            throw new PdfWerkException("LibreOffice is not available on this server.");

        // Everything for one conversion lives in a directory that is deleted afterwards, so a
        // crashed run cannot leak the caller's document onto the disk indefinitely.
        var work = Directory.CreateTempSubdirectory("pdfwerk-lo-");

        try
        {
            var inputName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(inputName))
                inputName = "document.docx";

            var inputPath = Path.Combine(work.FullName, inputName);
            await File.WriteAllBytesAsync(inputPath, source, ct).ConfigureAwait(false);

            var profileDir = Path.Combine(work.FullName, "profile");
            Directory.CreateDirectory(profileDir);

            await RunAsync(inputPath, work.FullName, profileDir, ct).ConfigureAwait(false);

            var produced = Directory.GetFiles(work.FullName, "*.pdf");
            if (produced.Length == 0)
                throw new PdfWerkException("LibreOffice did not produce a PDF from this document. It may be corrupt or password protected.");

            var content = await File.ReadAllBytesAsync(produced[0], ct).ConfigureAwait(false);
            PdfGuard.RequirePdf(content);

            return new PdfArtifact(content, FileNames.WithExtension(fileName, ".pdf"));
        }
        finally
        {
            TryDelete(work.FullName);
        }
    }

    private async Task RunAsync(string inputPath, string outputDir, string profileDir, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = _resolvedPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDir,
        };

        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--norestore");
        start.ArgumentList.Add("--nolockcheck");
        start.ArgumentList.Add("--nodefault");
        start.ArgumentList.Add("--nologo");
        start.ArgumentList.Add($"-env:UserInstallation={new Uri(profileDir).AbsoluteUri}");
        start.ArgumentList.Add("--convert-to");

        // The writer_pdf_Export filter is named explicitly; the bare "pdf" target can pick a
        // different filter depending on how the input's type is detected.
        start.ArgumentList.Add("pdf:writer_pdf_Export");
        start.ArgumentList.Add("--outdir");
        start.ArgumentList.Add(outputDir);
        start.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = start };

        if (!process.Start())
            throw new PdfWerkException("LibreOffice could not be started.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new PdfWerkException(
                $"Converting this document took longer than {_options.TimeoutSeconds} seconds and was stopped.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode == 0)
            return;

        logger.LogWarning(
            "LibreOffice exited with {Code}. stdout: {Out} stderr: {Err}",
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));

        throw new PdfWerkException("LibreOffice could not convert this document.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            // The process died on its own between the check and the kill; nothing to do.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file may still be held briefly after LibreOffice exits. The OS reclaims the
            // temp directory eventually; failing the request over it would be worse.
        }
    }

    /// <summary>Finds soffice, preferring an explicitly configured path.</summary>
    private string? Locate()
    {
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
            return File.Exists(_options.ExecutablePath) ? _options.ExecutablePath : null;

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return FromPath();
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { "ProgramFiles", "ProgramFiles(x86)" })
            {
                var dir = Environment.GetEnvironmentVariable(root);
                if (!string.IsNullOrEmpty(dir))
                    yield return Path.Combine(dir, "LibreOffice", "program", "soffice.exe");
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/LibreOffice.app/Contents/MacOS/soffice";
            yield break;
        }

        yield return "/usr/bin/soffice";
        yield return "/usr/bin/libreoffice";
        yield return "/usr/local/bin/soffice";
        yield return "/opt/libreoffice/program/soffice";
        yield return "/snap/bin/libreoffice";
    }

    private static string? FromPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        var names = OperatingSystem.IsWindows()
            ? new[] { "soffice.exe", "soffice.com" }
            : ["soffice", "libreoffice"];

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir.Trim(), name);
                }
                catch (ArgumentException)
                {
                    break;      // a malformed PATH entry; skip the whole directory
                }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
