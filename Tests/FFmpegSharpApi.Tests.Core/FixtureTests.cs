using FFmpeg.AutoGen.Abstractions;
using Xunit;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Drives every fixture in the manifest through FFmpegSharpApi and asserts the declared
/// accept / reject / recover_or_reject behaviour, capturing FFmpeg logs per test.
/// </summary>
[Collection(FFmpegCollection.Name)]
public sealed class FixtureTests
{
    [Theory]
    [MemberData(nameof(Fixtures.Ids), MemberType = typeof(Fixtures))]
    public void Fixture_behaves_as_declared(string id)
    {
        var fixture = Fixtures.ById(id);
        using var log = new TestLogScope(id);

        var path = TestPaths.Fixture(fixture.Path);
        if (path.Contains('%'))
        {
            var dir = Path.GetDirectoryName(path)!;
            Assert.True(Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any(),
                $"Image-sequence directory is empty: {dir}");
        }
        else
        {
            Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        }

        switch (fixture.Outcome)
        {
            case "accept":
                VerifyAccept(fixture, path, log);
                break;
            case "reject":
                VerifyReject(fixture, path);
                break;
            case "recover_or_reject":
                VerifyRecoverOrReject(fixture, path, log);
                break;
            default:
                throw new InvalidOperationException($"Unknown outcome '{fixture.Outcome}'");
        }
    }

    private static void VerifyAccept(Fixture fixture, string path, TestLogScope log)
    {
        var probe = MediaProbe.Probe(path, fixture.InputArgs);
        if (!probe.Success)
        {
            if (BuildCapabilities.IsOptionalUnavailable(fixture))
            {
                log.Note("Optional demuxer/decoder not present in this build; accept check skipped.");
                return;
            }
            Assert.Fail($"Expected to open '{fixture.Id}' but probe failed: {Interop.ErrorString(probe.ErrorCode)}");
        }

        if (probe.FirstVideo is { Width: 0 } missing && !BuildCapabilities.HasDecoder(missing.CodecName))
        {
            log.Note($"No decoder for '{missing.CodecName}' in this build; accept check skipped.");
            return;
        }

        StreamMatcher.AssertExpectedStreams(fixture.Expected, probe);

        if (fixture.Expected?.DurationRange is { Count: 2 } range && probe.Duration > 0)
            Assert.InRange(probe.Duration, range[0], range[1]);

        if (probe.FirstVideo is { } video)
        {
            var decode = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_VIDEO, 12);
            if (decode.FramesDecoded == 0 && (decode.NoDecoder || BuildCapabilities.DecodeMayBeUnavailable(video.CodecName)))
                log.Note($"No usable video decoder for '{video.CodecName}' in this build; decode skipped.");
            else
                Assert.True(decode.FramesDecoded > 0, $"No video frames decoded: {decode.Error}");
        }
        else if (probe.FirstAudio is { } audio)
        {
            var decode = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_AUDIO, 64);
            if (decode.FramesDecoded == 0 && (decode.NoDecoder || BuildCapabilities.DecodeMayBeUnavailable(audio.CodecName)))
                log.Note($"No usable audio decoder for '{audio.CodecName}' in this build; decode skipped.");
            else
                Assert.True(decode.FramesDecoded > 0, $"No audio frames decoded: {decode.Error}");
        }

        Assert.NotEmpty(log.Entries);
    }

    private static void VerifyReject(Fixture fixture, string path)
    {
        var probe = MediaProbe.Probe(path, fixture.InputArgs);
        var decoded = probe.Success && DecodedAnything(path, fixture.InputArgs);
        Assert.False(decoded, $"Expected '{fixture.Id}' to be rejected, but it opened and decoded frames.");
    }

    private static void VerifyRecoverOrReject(Fixture fixture, string path, TestLogScope log)
    {
        // Partial recovery or a clean error are both acceptable; the requirement is no crash.
        var probe = MediaProbe.Probe(path, fixture.InputArgs);
        if (probe.Success)
        {
            var video = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_VIDEO, 32);
            var audio = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_AUDIO, 32);
            log.Note($"recover_or_reject: opened, videoFrames={video.FramesDecoded}, audioFrames={audio.FramesDecoded}");
        }
        else
        {
            log.Note($"recover_or_reject: clean rejection ({Interop.ErrorString(probe.ErrorCode)})");
        }
    }

    private static bool DecodedAnything(string path, IReadOnlyList<string>? args)
    {
        foreach (var type in new[] { AVMediaType.AVMEDIA_TYPE_VIDEO, AVMediaType.AVMEDIA_TYPE_AUDIO })
        {
            if (MediaDecoder.Decode(path, args, type, 5).FramesDecoded > 0)
                return true;
        }
        return false;
    }
}
