namespace FFmpegSharpApi;

/// <summary>
/// Options controlling how <see cref="FFmpegRuntime.Initialize(FFmpegRuntimeOptions?)"/>
/// locates the native libraries and configures logging.
/// </summary>
public sealed class FFmpegRuntimeOptions
{
    /// <summary>
    /// Explicit directory containing the native FFmpeg libraries. When <see langword="null"/>
    /// the runtime probes <c>runtimes/&lt;rid&gt;/native</c> under the application base
    /// directory and the application base directory itself. Ignored on iOS and Mac Catalyst,
    /// where FFmpeg is statically linked.
    /// </summary>
    public string? NativeLibrariesPath { get; set; }

    /// <summary>Whether to route FFmpeg log output to the managed logging sinks.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Verbosity used when <see cref="EnableLogging"/> is set.</summary>
    public FFmpegLogLevel LogLevel { get; set; } = FFmpegLogLevel.Info;

    /// <summary>Optional file that receives every log line. The directory is created if needed.</summary>
    public string? LogFilePath { get; set; }

    /// <summary>Whether to also echo log lines to the console.</summary>
    public bool LogToConsole { get; set; }
}
