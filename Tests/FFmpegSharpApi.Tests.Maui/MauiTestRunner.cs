using System.IO.Compression;
using System.Text.Json;
using Xunit.Runners;

namespace FFmpegSharpApi.Tests.Maui;

/// <summary>A single executed test result.</summary>
public sealed record MauiTestItem(string Name, string Outcome, string? Message);

/// <summary>Aggregated mobile run result.</summary>
public sealed class MauiRunSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<MauiTestItem> Results { get; set; } = new();
}

/// <summary>
/// Unpacks the bundled fixture data on device and runs the shared Tests.Core via the xUnit
/// AssemblyRunner, writing results to the app data directory for CI retrieval.
/// </summary>
public static class MauiTestRunner
{
    private static bool _prepared;

    public static string OutputDirectory { get; private set; } = string.Empty;

    public static async Task PrepareAsync()
    {
        if (_prepared)
            return;

        var dataDir = Path.Combine(FileSystem.CacheDirectory, "testdata");
        if (!File.Exists(Path.Combine(dataDir, "manifest.json")))
        {
            Directory.CreateDirectory(dataDir);
            await using var zip = await FileSystem.OpenAppPackageFileAsync("testdata.zip");
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
            archive.ExtractToDirectory(dataDir, overwriteFiles: true);
        }

        OutputDirectory = Path.Combine(FileSystem.AppDataDirectory, "test-output");
        FFmpegSharpApi.Tests.Core.TestPaths.Configure(dataDir, OutputDirectory);
        _prepared = true;
    }

    public static MauiRunSummary RunAll(Action<MauiTestItem>? onResult = null)
    {
        var assembly = typeof(FFmpegSharpApi.Tests.Core.FixtureTests).Assembly;
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location))
            location = assembly.GetName().Name + ".dll";

        using var runner = AssemblyRunner.WithoutAppDomain(location);
        var results = new List<MauiTestItem>();
        using var done = new ManualResetEventSlim(false);

        void Record(MauiTestItem item)
        {
            lock (results)
                results.Add(item);
            onResult?.Invoke(item);
        }

        runner.OnTestPassed = i => Record(new MauiTestItem(i.TestDisplayName, "Passed", null));
        runner.OnTestFailed = i => Record(new MauiTestItem(i.TestDisplayName, "Failed", i.ExceptionMessage));
        runner.OnTestSkipped = i => Record(new MauiTestItem(i.TestDisplayName, "Skipped", i.SkipReason));
        runner.OnExecutionComplete = _ => done.Set();

        runner.Start();
        done.Wait();

        return new MauiRunSummary
        {
            Results = results,
            Total = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Skipped = results.Count(r => r.Outcome == "Skipped"),
        };
    }

    public static async Task WriteResultsAsync(MauiRunSummary summary)
    {
        if (string.IsNullOrEmpty(OutputDirectory))
            return;

        Directory.CreateDirectory(OutputDirectory);
        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(OutputDirectory, "mobile-results.json"), json);
        // Sentinel the CI harness waits on.
        await File.WriteAllTextAsync(Path.Combine(OutputDirectory, "DONE.txt"),
            summary.Failed == 0 ? "PASS" : "FAIL");
    }
}
