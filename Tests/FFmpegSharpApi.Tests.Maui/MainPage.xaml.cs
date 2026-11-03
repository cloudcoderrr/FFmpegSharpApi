using System.Collections.ObjectModel;

namespace FFmpegSharpApi.Tests.Maui;

public partial class MainPage : ContentPage
{
    private readonly ObservableCollection<string> _items = new();
    private bool _started;

    public MainPage()
    {
        InitializeComponent();
        ResultsView.ItemsSource = _items;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;
        _started = true;

        try
        {
            await MauiTestRunner.PrepareAsync();
            HeaderLabel.Text = DescribeBuild();
            SummaryLabel.Text = "Running…";
            Console.WriteLine("##MOBILE_HEADER## " + HeaderLabel.Text);

            var summary = await Task.Run(() => MauiTestRunner.RunAll(item =>
                MainThread.BeginInvokeOnMainThread(() => _items.Add($"{item.Outcome,-7} {item.Name}"))));

            SummaryLabel.Text = $"Total={summary.Total}  Passed={summary.Passed}  Failed={summary.Failed}  Skipped={summary.Skipped}";
            await MauiTestRunner.WriteResultsAsync(summary);
            Console.WriteLine("##MOBILE_LOGCOUNT## " + FFmpegSharpApi.FFmpegLog.CallbackCount);
            Console.WriteLine("##MOBILE_LOGREG## " + (FFmpegSharpApi.FFmpegLog.NativeRegistrationStatus ?? "(default)"));
            foreach (var f in summary.Results.Where(r => r.Outcome == "Failed").Take(3))
                Console.WriteLine($"##MOBILE_FAIL## {f.Name} :: {f.Message}");
            ReportToCi($"Total={summary.Total} Passed={summary.Passed} Failed={summary.Failed} Skipped={summary.Skipped}",
                summary.Failed == 0 ? "PASS" : "FAIL");
        }
        catch (Exception ex)
        {
            SummaryLabel.Text = "Error: " + ex.Message;
            ReportToCi("Unhandled error: " + ex, "FAIL");
        }
    }

    // Emits machine-readable markers to stdout (captured via logcat / simctl --console)
    // then terminates the process so the CI harness run returns.
    private static void ReportToCi(string summary, string verdict)
    {
        Console.WriteLine("##MOBILE_RESULT## " + summary);
        Console.WriteLine("##MOBILE_DONE## " + verdict);
        Console.Out.Flush();

        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Environment.Exit(verdict == "PASS" ? 0 : 1);
        });
    }

    private static string DescribeBuild()
    {
        try
        {
            FFmpegSharpApi.FFmpegRuntime.Initialize(new FFmpegSharpApi.FFmpegRuntimeOptions { EnableLogging = false });
            return $"FFmpeg {FFmpegSharpApi.FFmpegInfo.VersionInfo} — {FFmpegSharpApi.FFmpegInfo.License}";
        }
        catch (Exception ex)
        {
            return "FFmpeg native libraries not loaded: " + ex.Message;
        }
    }
}
