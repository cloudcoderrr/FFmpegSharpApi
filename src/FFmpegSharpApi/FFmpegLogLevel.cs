namespace FFmpegSharpApi;

/// <summary>
/// FFmpeg log verbosity, mapped to the native <c>AV_LOG_*</c> constants.
/// </summary>
public enum FFmpegLogLevel
{
    /// <summary>Print no output.</summary>
    Quiet = -8,
    /// <summary>Something went really wrong; crash imminent.</summary>
    Panic = 0,
    /// <summary>Unrecoverable error.</summary>
    Fatal = 8,
    /// <summary>Recoverable error.</summary>
    Error = 16,
    /// <summary>Warning.</summary>
    Warning = 24,
    /// <summary>Standard information.</summary>
    Info = 32,
    /// <summary>Detailed information.</summary>
    Verbose = 40,
    /// <summary>Debugging information.</summary>
    Debug = 48,
    /// <summary>Extremely verbose debugging information.</summary>
    Trace = 56,
}
