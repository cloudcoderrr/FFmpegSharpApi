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

            var summary = await Task.Run(() => MauiTestRunner.RunAll(item =>
                MainThread.BeginInvokeOnMainThread(() => _items.Add($"{item.Outcome,-7} {item.Name}"))));

            SummaryLabel.Text = $"Total={summary.Total}  Passed={summary.Passed}  Failed={summary.Failed}  Skipped={summary.Skipped}";
            await MauiTestRunner.WriteResultsAsync(summary);
        }
        catch (Exception ex)
        {
            SummaryLabel.Text = "Error: " + ex.Message;
        }
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
