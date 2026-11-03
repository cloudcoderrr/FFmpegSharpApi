using Xunit;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Initializes FFmpeg once for the whole test run and records the build identity.
/// Native libraries are located via the <c>FFMPEG_NATIVE_DIR</c> environment variable when set,
/// otherwise the default probing is used.
/// </summary>
public sealed class FFmpegEnvironment : IDisposable
{
    public FFmpegEnvironment()
    {
        FFmpegRuntime.Initialize(new FFmpegRuntimeOptions
        {
            NativeLibrariesPath = Environment.GetEnvironmentVariable("FFMPEG_NATIVE_DIR"),
            EnableLogging = true,
            LogLevel = FFmpegLogLevel.Verbose,
            LogToConsole = false,
        });

        var summary = Path.Combine(TestPaths.OutputDir, "ffmpeg-build.txt");
        Directory.CreateDirectory(TestPaths.OutputDir);
        File.WriteAllText(summary,
            $"version={FFmpegInfo.VersionInfo}\n" +
            $"license={FFmpegInfo.License}\n" +
            $"gpl={FFmpegInfo.IsGplBuild}\n" +
            $"avcodec={FFmpegInfo.FormatVersion(FFmpegInfo.AvCodecVersion)}\n" +
            $"configuration={FFmpegInfo.Configuration}\n");
    }

    public string Version => FFmpegInfo.VersionInfo;

    public void Dispose()
    {
    }
}

/// <summary>Collection that shares a single initialized FFmpeg environment.</summary>
[CollectionDefinition(Name)]
public sealed class FFmpegCollection : ICollectionFixture<FFmpegEnvironment>
{
    public const string Name = "ffmpeg";
}
