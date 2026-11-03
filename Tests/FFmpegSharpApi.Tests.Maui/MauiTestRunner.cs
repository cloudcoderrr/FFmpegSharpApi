using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

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
/// Unpacks the bundled fixture data on device and runs the shared Tests.Core suite against
/// the loaded test assembly using xUnit's in-process reflection API (no assembly file is
/// required, which is essential on mobile where managed assemblies are embedded in the app).
/// Results are written to the app data directory for CI retrieval.
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
            using var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Read);
            archive.ExtractToDirectory(dataDir, overwriteFiles: true);
        }

        OutputDirectory = Path.Combine(FileSystem.AppDataDirectory, "test-output");
        FFmpegSharpApi.Tests.Core.TestPaths.Configure(dataDir, OutputDirectory);
        _prepared = true;
    }

    public static MauiRunSummary RunAll(Action<MauiTestItem>? onResult = null)
    {
        var assembly = typeof(FFmpegSharpApi.Tests.Core.FixtureTests).Assembly;
        var results = new List<MauiTestItem>();

        void Record(MauiTestItem item)
        {
            lock (results)
                results.Add(item);
            onResult?.Invoke(item);
        }

        var diagnostics = new NullSink();
        using var framework = new XunitTestFramework(diagnostics);
        var assemblyInfo = Reflector.Wrap(assembly);

        // --- discovery (reflection over the already-loaded assembly) ---
        var testCases = new List<ITestCase>();
        using (var discoverer = framework.GetDiscoverer(assemblyInfo))
        using (var discoverySink = new DiscoverySink(testCases))
        {
            discoverer.Find(includeSourceInformation: false, discoverySink, TestFrameworkOptions.ForDiscovery());
            discoverySink.Finished.WaitOne();
        }

        // --- execution ---
        using (var executor = framework.GetExecutor(assembly.GetName()))
        using (var executionSink = new ExecutionSink(Record))
        {
            executor.RunTests(testCases, executionSink, TestFrameworkOptions.ForExecution());
            executionSink.Finished.WaitOne();
        }

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

    private sealed class DiscoverySink : LongLivedMarshalByRefObject, IMessageSink, IDisposable
    {
        private readonly List<ITestCase> _cases;
        public ManualResetEvent Finished { get; } = new(false);

        public DiscoverySink(List<ITestCase> cases) => _cases = cases;
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage discovered)
                _cases.Add(discovered.TestCase);
            else if (message is IDiscoveryCompleteMessage)
                Finished.Set();
            return true;
        }

        public void Dispose() => Finished.Dispose();
    }

    private sealed class ExecutionSink : LongLivedMarshalByRefObject, IMessageSink, IDisposable
    {
        private readonly Action<MauiTestItem> _record;
        public ManualResetEvent Finished { get; } = new(false);

        public ExecutionSink(Action<MauiTestItem> record) => _record = record;

        public bool OnMessage(IMessageSinkMessage message)
        {
            switch (message)
            {
                case ITestPassed passed:
                    _record(new MauiTestItem(passed.Test.DisplayName, "Passed", null));
                    break;
                case ITestFailed failed:
                    _record(new MauiTestItem(failed.Test.DisplayName, "Failed",
                        string.Join(" | ", failed.Messages)));
                    break;
                case ITestSkipped skipped:
                    _record(new MauiTestItem(skipped.Test.DisplayName, "Skipped", skipped.Reason));
                    break;
                case ITestAssemblyFinished:
                    Finished.Set();
                    break;
            }
            return true;
        }

        public void Dispose() => Finished.Dispose();
    }

    private sealed class NullSink : LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }
}
