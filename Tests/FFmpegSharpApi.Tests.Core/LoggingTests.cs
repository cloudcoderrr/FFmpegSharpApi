using FFmpeg.AutoGen.Abstractions;
using Xunit;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>Verifies that FFmpeg logging is captured and that build info is reachable.</summary>
[Collection(FFmpegCollection.Name)]
public sealed class LoggingTests
{
    [Fact]
    public void Build_info_is_available()
    {
        Assert.False(string.IsNullOrWhiteSpace(FFmpegInfo.VersionInfo));
        Assert.Contains("9.0", FFmpegInfo.VersionInfo);
        Assert.False(string.IsNullOrWhiteSpace(FFmpegInfo.License));
        Assert.False(string.IsNullOrWhiteSpace(FFmpegInfo.Configuration));
    }

    [Fact]
    public unsafe void Logging_captures_dump_format_output()
    {
        using var log = new TestLogScope("logging.dump_format");
        var path = TestPaths.Fixture("video/h264_aac_stereo_faststart.mp4");

        AVFormatContext* fmt = null;
        Assert.True(ffmpeg.avformat_open_input(&fmt, path, null, null) == 0);
        ffmpeg.avformat_find_stream_info(fmt, null);
        ffmpeg.av_dump_format(fmt, 0, path, 0);
        ffmpeg.avformat_close_input(&fmt);

        Assert.NotEmpty(log.Entries);
        Assert.True(File.Exists(log.LogFile));
    }
}
