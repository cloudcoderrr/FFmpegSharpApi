using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi;

/// <summary>
/// Convenience accessors for FFmpeg build and version information. Call
/// <see cref="FFmpegRuntime.Initialize(FFmpegRuntimeOptions?)"/> before using these members.
/// </summary>
public static class FFmpegInfo
{
    /// <summary>The FFmpeg version string, for example <c>9.0.1</c>.</summary>
    public static string VersionInfo => ffmpeg.av_version_info();

    /// <summary>The full <c>./configure</c> line the libraries were built with.</summary>
    public static string Configuration => ffmpeg.avutil_configuration();

    /// <summary>The effective FFmpeg license, for example <c>LGPL version 3 or later</c>.</summary>
    public static string License => ffmpeg.avutil_license();

    /// <summary>Whether the loaded build enables GPL components (libx264/libx265 etc.).</summary>
    public static bool IsGplBuild => Configuration.Contains("--enable-gpl", StringComparison.Ordinal);

    /// <summary>The <c>libavutil</c> version packed into a single integer.</summary>
    public static uint AvUtilVersion => ffmpeg.avutil_version();

    /// <summary>The <c>libavcodec</c> version packed into a single integer.</summary>
    public static uint AvCodecVersion => ffmpeg.avcodec_version();

    /// <summary>The <c>libavformat</c> version packed into a single integer.</summary>
    public static uint AvFormatVersion => ffmpeg.avformat_version();

    /// <summary>Formats a packed FFmpeg version integer as <c>major.minor.micro</c>.</summary>
    public static string FormatVersion(uint version)
        => $"{version >> 16}.{(version >> 8) & 0xFF}.{version & 0xFF}";
}
