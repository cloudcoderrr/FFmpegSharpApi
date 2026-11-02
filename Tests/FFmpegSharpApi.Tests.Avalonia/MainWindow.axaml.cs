using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace FFmpegSharpApi.Tests.Avalonia;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<string> _items = new();

    public MainWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _items;
        BuildInfoText.Text = DescribeBuild();
    }

    private async void OnRunClick(object? sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        _items.Clear();
        SummaryText.Text = "Running…";

        var summary = await Task.Run(() => TestExecution.RunAll(item =>
            Dispatcher.UIThread.Post(() => _items.Add($"{item.Outcome,-7} {item.Name}"))));

        SummaryText.Text = $"Total={summary.Total}  Passed={summary.Passed}  Failed={summary.Failed}  Skipped={summary.Skipped}";
        RunButton.IsEnabled = true;
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
