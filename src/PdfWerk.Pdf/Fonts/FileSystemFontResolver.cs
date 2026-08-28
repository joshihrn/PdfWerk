using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace PdfWerk.Pdf.Fonts;

/// <summary>
/// Resolves typefaces from the fonts installed on the host. PDFsharp ships no fonts of its
/// own and has no default resolver outside Windows, so without this the whole rendering path
/// throws the moment it runs in a Linux container.
/// </summary>
/// <remarks>
/// Families are matched loosely: an exact file match first, then a per-family list of
/// substitutes that covers the metric-compatible open fonts shipped by most distributions.
/// Whatever is found is cached for the process lifetime — font files are large and the same
/// handful get requested on every render.
/// </remarks>
public sealed class FileSystemFontResolver : IFontResolver
{
    private static readonly string[] SearchPaths = BuildSearchPaths();

    private static readonly Dictionary<string, string[]> Substitutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["helvetica"] = ["Helvetica", "Arial", "LiberationSans", "DejaVuSans", "NimbusSans", "FreeSans"],
        ["arial"] = ["Arial", "LiberationSans", "DejaVuSans", "NimbusSans", "FreeSans"],
        ["times new roman"] = ["Times New Roman", "LiberationSerif", "DejaVuSerif", "NimbusRoman", "FreeSerif"],
        ["times"] = ["Times New Roman", "LiberationSerif", "DejaVuSerif", "NimbusRoman", "FreeSerif"],
        ["georgia"] = ["Georgia", "LiberationSerif", "DejaVuSerif", "FreeSerif"],
        ["courier new"] = ["Courier New", "LiberationMono", "DejaVuSansMono", "NimbusMono", "FreeMono"],
        ["courier"] = ["Courier New", "LiberationMono", "DejaVuSansMono", "NimbusMono", "FreeMono"],
        ["consolas"] = ["Consolas", "LiberationMono", "DejaVuSansMono", "FreeMono"],
        ["verdana"] = ["Verdana", "DejaVuSans", "LiberationSans", "FreeSans"],
        ["calibri"] = ["Calibri", "Carlito", "LiberationSans", "DejaVuSans"],
        ["cambria"] = ["Cambria", "Caladea", "LiberationSerif", "DejaVuSerif"],
        ["segoe ui"] = ["Segoe UI", "DejaVuSans", "LiberationSans"],
    };

    /// <summary>Tried when the requested family yields nothing at all.</summary>
    private static readonly string[] LastResort =
        ["DejaVuSans", "LiberationSans", "FreeSans", "NimbusSans", "Arial", "Helvetica"];

    private readonly ConcurrentDictionary<string, byte[]> _fontData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? GetFont(string faceName) =>
        _fontData.TryGetValue(faceName, out var data) ? data : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var faceName = $"{familyName}|{(isBold ? "b" : string.Empty)}{(isItalic ? "i" : string.Empty)}";

        var path = _resolved.GetOrAdd(faceName, _ => Locate(familyName, isBold, isItalic));
        if (path is null)
            return null;

        _fontData.GetOrAdd(faceName, _ => File.ReadAllBytes(path));

        // The file already carries the requested weight/slant, so tell PDFsharp not to
        // simulate them on top — that would double-embolden.
        return new FontResolverInfo(faceName, false, false);
    }

    private static string? Locate(string familyName, bool isBold, bool isItalic)
    {
        var candidates = Substitutes.TryGetValue(familyName.Trim(), out var subs)
            ? subs
            : [familyName, .. LastResort];

        foreach (var candidate in candidates)
        {
            var hit = FindFile(candidate, isBold, isItalic);
            if (hit is not null)
                return hit;
        }

        foreach (var candidate in LastResort)
        {
            var hit = FindFile(candidate, isBold, isItalic) ?? FindFile(candidate, false, false);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    private static string? FindFile(string family, bool bold, bool italic)
    {
        var compact = Compact(family);

        // Most specific style suffix first, degrading to the regular face.
        var suffixes = (bold, italic) switch
        {
            (true, true) => new[] { "bolditalic", "boldoblique", "bi", "z", "bold" },
            (true, false) => ["bold", "bd", "b"],
            (false, true) => ["italic", "oblique", "i"],
            _ => ["regular", string.Empty],
        };

        var files = EnumerateFontFiles().ToList();

        foreach (var suffix in suffixes)
        {
            var wanted = compact + suffix;
            var match = files.FirstOrDefault(f => Compact(Path.GetFileNameWithoutExtension(f)) == wanted);
            if (match is not null)
                return match;
        }

        if (!bold && !italic)
        {
            // A regular face is often named with no suffix at all.
            var bare = files.FirstOrDefault(f => Compact(Path.GetFileNameWithoutExtension(f)) == compact);
            if (bare is not null)
                return bare;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFontFiles() => FontFileCache.Value;

    private static readonly Lazy<IReadOnlyList<string>> FontFileCache = new(() =>
    {
        var results = new List<string>();
        foreach (var dir in SearchPaths.Where(Directory.Exists))
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(dir, "*.ttf", SearchOption.AllDirectories));
                results.AddRange(Directory.EnumerateFiles(dir, "*.otf", SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException)
            {
                // A locked-down font directory is not fatal; other paths usually cover us.
            }
            catch (IOException)
            {
            }
        }

        return results;
    });

    private static string Compact(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string[] BuildSearchPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["/System/Library/Fonts", "/Library/Fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts")];
        }

        return
        [
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts"),
        ];
    }

    /// <summary>Installs this resolver globally. Safe to call more than once.</summary>
    public static void Install()
    {
        if (GlobalFontSettings.FontResolver is FileSystemFontResolver)
            return;

        GlobalFontSettings.FontResolver = new FileSystemFontResolver();
    }
}
