using FFmpeg.AutoGen.Abstractions;
using Xunit;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Produces verification artifacts (PNG snapshots and WAV audio snippets) from the fixtures,
/// written under the test output directory for manual review in CI.
/// </summary>
[Collection(FFmpegCollection.Name)]
public sealed class ArtifactTests
{
    [Theory]
    [MemberData(nameof(Fixtures.IdsUnderFolder), "video/", MemberType = typeof(Fixtures))]
    public void Video_snapshot_is_produced(string id)
    {
        var fixture = Fixtures.ById(id);
        using var log = new TestLogScope($"snapshot.{id}");
        var path = TestPaths.Fixture(fixture.Path);

        var probe = MediaProbe.Probe(path, fixture.InputArgs);
        if (probe.FirstVideo is null)
            return;

        var check = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_VIDEO, 1);
        if (check.NoDecoder)
        {
            log.Note($"No video decoder for '{probe.FirstVideo.CodecName}'; snapshot skipped.");
            return;
        }

        var output = Path.Combine(TestPaths.SnapshotsDir, id + ".png");
        Assert.True(MediaDecoder.SaveVideoSnapshot(path, fixture.InputArgs, output),
            $"Could not produce a snapshot for '{id}'.");
        Assert.True(new FileInfo(output).Length > 0, "Snapshot file is empty.");
    }

    [Theory]
    [MemberData(nameof(Fixtures.IdsUnderFolder), "audio/", MemberType = typeof(Fixtures))]
    public void Audio_snippet_is_produced(string id)
    {
        var fixture = Fixtures.ById(id);
        using var log = new TestLogScope($"audio.{id}");
        var path = TestPaths.Fixture(fixture.Path);

        var probe = MediaProbe.Probe(path, fixture.InputArgs);
        if (probe.FirstAudio is null)
            return;

        var check = MediaDecoder.Decode(path, fixture.InputArgs, AVMediaType.AVMEDIA_TYPE_AUDIO, 1);
        if (check.NoDecoder)
        {
            log.Note($"No audio decoder for '{probe.FirstAudio.CodecName}'; snippet skipped.");
            return;
        }

        var output = Path.Combine(TestPaths.AudioDir, id + ".wav");
        Assert.True(MediaDecoder.SaveAudioSnippet(path, fixture.InputArgs, output, seconds: 1.5),
            $"Could not produce an audio snippet for '{id}'.");
        Assert.True(new FileInfo(output).Length > 44, "WAV file has no samples.");
    }
}
