using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit.Runners;

namespace FFmpegSharpApi.Tests.Avalonia;

/// <summary>A single executed test result.</summary>
public sealed record TestItem(string Name, string Outcome, string? Message, decimal Seconds);

/// <summary>Aggregated run result.</summary>
public sealed class RunSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<TestItem> Results { get; set; } = new();
}

/// <summary>Runs the shared xUnit test core in-process via the xUnit AssemblyRunner.</summary>
public static class TestExecution
{
    public static RunSummary RunAll(Action<TestItem>? onResult = null)
    {
        var assemblyPath = typeof(FFmpegSharpApi.Tests.Core.FixtureTests).Assembly.Location;
        using var runner = AssemblyRunner.WithoutAppDomain(assemblyPath);
        var results = new List<TestItem>();
        using var done = new ManualResetEventSlim(false);

        void Record(TestItem item)
        {
            lock (results)
                results.Add(item);
            onResult?.Invoke(item);
        }

        runner.OnTestPassed = i => Record(new TestItem(i.TestDisplayName, "Passed", null, i.ExecutionTime));
        runner.OnTestFailed = i => Record(new TestItem(i.TestDisplayName, "Failed", i.ExceptionMessage, i.ExecutionTime));
        runner.OnTestSkipped = i => Record(new TestItem(i.TestDisplayName, "Skipped", i.SkipReason, 0));
        runner.OnExecutionComplete = _ => done.Set();

        runner.Start();
        done.Wait();

        return new RunSummary
        {
            Results = results,
            Total = results.Count,
            Passed = results.Count(r => r.Outcome == "Passed"),
            Failed = results.Count(r => r.Outcome == "Failed"),
            Skipped = results.Count(r => r.Outcome == "Skipped"),
        };
    }
}
