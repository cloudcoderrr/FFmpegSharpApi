using System;
using System.IO;
using System.Text.Json;

namespace FFmpegSharpApi.Tests.Avalonia;

/// <summary>Non-UI test run used in CI. Prints results and returns an exit code.</summary>
internal static class HeadlessRunner
{
    public static int Run()
    {
        Console.WriteLine("FFmpegSharpApi desktop tests (headless)");
        var summary = TestExecution.RunAll(r => Console.WriteLine($"{r.Outcome,-7} {r.Name}"));
        Console.WriteLine($"Total={summary.Total} Passed={summary.Passed} Failed={summary.Failed} Skipped={summary.Skipped}");

        var outputDir = Environment.GetEnvironmentVariable("FFMPEG_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(
                Path.Combine(outputDir, "desktop-results.json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        return summary.Failed == 0 ? 0 : 1;
    }
}
