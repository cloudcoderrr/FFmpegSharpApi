using System;
using System.Linq;
using Avalonia;

namespace FFmpegSharpApi.Tests.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless mode runs every test and returns a process exit code for CI.
        if (args.Contains("--ci") || args.Contains("--headless"))
            return HeadlessRunner.Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
