using System.Text.Json;
using Xunit;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>Matches probed streams against the expected-stream specs from the manifest.</summary>
internal static class StreamMatcher
{
    public static void AssertExpectedStreams(Expected? expected, ProbeResult probe)
    {
        if (expected?.Streams is null)
            return;

        foreach (var spec in expected.Streams)
        {
            var matched = probe.Streams.Any(stream => Matches(stream, spec));
            Assert.True(matched,
                $"No probed stream matched expected {Describe(spec)}.\nProbed: {Describe(probe)}");
        }
    }

    private static bool Matches(ProbeStream stream, Dictionary<string, JsonElement> spec)
    {
        foreach (var (key, value) in spec)
        {
            var ok = key switch
            {
                "codec_type" => StrEq(stream.CodecType, value),
                "codec_name" => StrEq(stream.CodecName, value),
                "width" => stream.Width == Int(value),
                "height" => stream.Height == Int(value),
                "pix_fmt" => StrEq(stream.PixFmt, value),
                "sample_rate" => stream.SampleRate == Int(value),
                "channels" => stream.Channels == Int(value),
                "profile" => StrEq(stream.Profile, value),
                "has_b_frames" => stream.HasBFrames == Int(value),
                "min_b_frames" => stream.HasBFrames >= Int(value),
                "r_frame_rate" => StrEq(stream.RFrameRate, value),
                _ => true, // ignore keys we do not model
            };
            if (!ok)
                return false;
        }
        return true;
    }

    private static int Int(JsonElement e)
        => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!);

    private static bool StrEq(string? actual, JsonElement e)
    {
        var expected = e.ValueKind == JsonValueKind.Number ? e.GetRawText() : e.GetString();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(Dictionary<string, JsonElement> spec)
        => "{" + string.Join(", ", spec.Select(kv => $"{kv.Key}={kv.Value}")) + "}";

    private static string Describe(ProbeResult probe)
        => string.Join(" | ", probe.Streams.Select(s =>
            $"{s.CodecType}:{s.CodecName} {s.Width}x{s.Height} {s.PixFmt} {s.SampleRate}Hz {s.Channels}ch"));
}
