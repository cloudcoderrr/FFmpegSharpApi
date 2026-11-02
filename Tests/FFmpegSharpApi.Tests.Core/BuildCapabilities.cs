using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Describes what the loaded FFmpeg build can actually do, so tests can distinguish a real
/// failure from an intentionally omitted optional component (e.g. an LGPL build without the
/// GPL encoders, or a build without the dav1d AV1 decoder or libxml2 DASH demuxer).
/// </summary>
internal static unsafe class BuildCapabilities
{
    private static readonly HashSet<string> OptionalSoftwareDecode =
        new(StringComparer.OrdinalIgnoreCase) { "av1" };

    public static bool HasDemuxer(string name) => ffmpeg.av_find_input_format(name) != null;

    public static bool HasEncoder(string name) => ffmpeg.avcodec_find_encoder_by_name(name) != null;

    public static bool HasDecoder(string name) => ffmpeg.avcodec_find_decoder_by_name(name) != null;

    /// <summary>True for codecs whose software decoder depends on an external library.</summary>
    public static bool DecodeMayBeUnavailable(string codecName) => OptionalSoftwareDecode.Contains(codecName);

    /// <summary>True when an accept fixture needs an optional component this build omits.</summary>
    public static bool IsOptionalUnavailable(Fixture fixture)
    {
        if (fixture.Path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) && !HasDemuxer("dash"))
            return true;
        return false;
    }
}
