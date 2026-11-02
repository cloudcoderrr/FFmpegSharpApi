using System.Runtime.InteropServices;

namespace FFmpegSharpApi;

/// <summary>
/// Locates the directory that contains the native FFmpeg libraries for the current
/// runtime identifier. Used only on the dynamically loaded platforms.
/// </summary>
internal static class NativeLibraryLocator
{
    private static readonly string[] LibraryStems =
        { "avutil", "avcodec", "avformat", "avfilter", "swscale", "swresample", "avdevice" };

    /// <summary>
    /// Returns the best directory to load the native libraries from, or an empty string
    /// to let the OS loader resolve by soname.
    /// </summary>
    public static string Resolve()
    {
        var baseDir = AppContext.BaseDirectory;
        var rid = RuntimeInformation.RuntimeIdentifier;

        var candidates = new[]
        {
            Path.Combine(baseDir, "runtimes", rid, "native"),
            Path.Combine(baseDir, "runtimes", NormalizeRid(rid), "native"),
            baseDir,
        };

        foreach (var candidate in candidates)
        {
            if (ContainsFFmpeg(candidate))
                return candidate;
        }

        // Nothing matched; the resolver falls back to loading by soname.
        return Directory.Exists(candidates[0]) ? candidates[0] : baseDir;
    }

    private static bool ContainsFFmpeg(string directory)
    {
        if (!Directory.Exists(directory))
            return false;

        foreach (var stem in LibraryStems)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (name.Contains(stem, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    // Maps some common host RIDs to the base RID folders shipped by the native packages.
    private static string NormalizeRid(string rid)
    {
        if (rid.StartsWith("win", StringComparison.Ordinal))
        {
            if (rid.Contains("arm64")) return "win-arm64";
            if (rid.Contains("x86")) return "win-x86";
            return "win-x64";
        }
        if (rid.StartsWith("osx", StringComparison.Ordinal))
            return rid.Contains("arm64") ? "osx-arm64" : "osx-x64";
        if (rid.StartsWith("linux", StringComparison.Ordinal))
        {
            if (rid.Contains("arm64")) return "linux-arm64";
            if (rid.Contains("arm")) return "linux-arm";
            if (rid.Contains("x86")) return "linux-x86";
            return "linux-x64";
        }
        return rid;
    }
}
