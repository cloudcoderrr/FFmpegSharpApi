namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Resolves the test data directory (fixtures + manifest) and the output directory for
/// logs and media artifacts, across desktop and mobile hosts.
/// </summary>
public static class TestPaths
{
    private static string? _dataDir;
    private static string? _outputDir;

    /// <summary>Directory that contains <c>manifest.json</c> and the fixture folders.</summary>
    public static string DataDir => _dataDir ??= ResolveDataDir();

    /// <summary>Root directory for test outputs (logs, snapshots, audio).</summary>
    public static string OutputDir => _outputDir ??= ResolveOutputDir();

    public static string LogsDir => EnsureDir(Path.Combine(OutputDir, "logs"));
    public static string SnapshotsDir => EnsureDir(Path.Combine(OutputDir, "snapshots"));
    public static string AudioDir => EnsureDir(Path.Combine(OutputDir, "audio"));
    public static string ProbeDir => EnsureDir(Path.Combine(OutputDir, "probe"));

    public static string ManifestPath => Path.Combine(DataDir, "manifest.json");

    /// <summary>Resolves a fixture's absolute path from its manifest-relative path.</summary>
    public static string Fixture(string relativePath)
        => Path.GetFullPath(Path.Combine(DataDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Overrides the resolved directories (used by the mobile runners).</summary>
    public static void Configure(string dataDir, string outputDir)
    {
        _dataDir = dataDir;
        _outputDir = outputDir;
        Directory.CreateDirectory(outputDir);
    }

    private static string ResolveDataDir()
    {
        var env = Environment.GetEnvironmentVariable("FFMPEG_TEST_DATA");
        if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "manifest.json")))
            return env;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "Tests", "Data");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
                return candidate;

            var bundled = Path.Combine(dir, "Data");
            if (File.Exists(Path.Combine(bundled, "manifest.json")))
                return bundled;

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException(
            "Could not locate the test data directory. Set FFMPEG_TEST_DATA or call TestPaths.Configure.");
    }

    private static string ResolveOutputDir()
    {
        var env = Environment.GetEnvironmentVariable("FFMPEG_TEST_OUTPUT");
        var dir = !string.IsNullOrEmpty(env)
            ? env
            : Path.Combine(FindRepoRootOrBase(), "test-output");
        return EnsureDir(dir);
    }

    private static string FindRepoRootOrBase()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
