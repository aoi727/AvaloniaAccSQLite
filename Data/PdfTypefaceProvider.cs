using SkiaSharp;

namespace AccountingApp.Data;

internal static class PdfTypefaceProvider
{
    private static readonly string[] BundledFontFileNames =
    [
        "NotoSansCJKjp-Regular.otf",
        "NotoSansJP-Regular.otf",
        "NotoSerifCJKjp-Regular.otf",
        "NotoSerifJP-Regular.otf"
    ];

    private static readonly string[] SystemFontFileNames =
    [
        // Windows
        "YuGothR.ttc",
        "YuGothM.ttc",
        "meiryo.ttc",
        "msgothic.ttc",

        // Linux distributions commonly package one of these Japanese fonts.
        "NotoSansCJK-Regular.ttc",
        "NotoSansCJKjp-Regular.otf",
        "NotoSansJP-Regular.otf",
        "NotoSerifCJK-Regular.ttc",
        "NotoSerifCJKjp-Regular.otf",
        "NotoSerifJP-Regular.otf",
        "TakaoPGothic.ttf",
        "TakaoGothic.ttf",
        "VL-PGothic-Regular.ttf",
        "ipagp.ttf",
        "ipag.ttf"
    ];

    private static readonly string[] LinuxFontDirectories =
    [
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/usr/share/fonts/opentype",
        "/usr/share/fonts/truetype"
    ];

    public static SKTypeface LoadJapaneseTypeface()
    {
        foreach (var path in GetCandidateFontPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var typeface = SKTypeface.FromFile(path);
            if (typeface is not null)
            {
                return typeface;
            }
        }

        return SKTypeface.Default;
    }

    private static IEnumerable<string> GetCandidateFontPaths()
    {
        foreach (var baseDirectory in GetBundledFontDirectories())
        {
            foreach (var fileName in BundledFontFileNames)
            {
                yield return Path.Combine(baseDirectory, fileName);
            }
        }

        var windowsFontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrWhiteSpace(windowsFontsFolder))
        {
            foreach (var fileName in SystemFontFileNames)
            {
                yield return Path.Combine(windowsFontsFolder, fileName);
            }
        }

        foreach (var directory in LinuxFontDirectories.Where(Directory.Exists))
        {
            foreach (var fileName in SystemFontFileNames)
            {
                yield return Path.Combine(directory, fileName);
            }

            foreach (var path in EnumerateFontsSafely(directory))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> GetBundledFontDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Fonts");
        yield return Path.Combine(Environment.CurrentDirectory, "Fonts");
        yield return Path.Combine(Environment.CurrentDirectory, "AccountingApp", "Fonts");
    }

    private static IEnumerable<string> EnumerateFontsSafely(string directory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (SystemFontFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }
}
