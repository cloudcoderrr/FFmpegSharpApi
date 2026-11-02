using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>The machine-readable fixture contract (<c>Tests/Data/manifest.json</c>).</summary>
public sealed class Manifest
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("fixtures")] public List<Fixture> Fixtures { get; set; } = new();

    public static Manifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.Manifest)
               ?? throw new InvalidDataException($"Could not parse manifest at {path}");
    }
}

/// <summary>A single fixture entry.</summary>
public sealed class Fixture
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("scenario")] public string Scenario { get; set; } = string.Empty;
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();

    /// <summary>One of <c>accept</c>, <c>reject</c> or <c>recover_or_reject</c>.</summary>
    [JsonPropertyName("outcome")] public string Outcome { get; set; } = "accept";

    /// <summary>Extra demuxer input options required to open the fixture (raw formats).</summary>
    [JsonPropertyName("input_args")] public List<string> InputArgs { get; set; } = new();

    [JsonPropertyName("expected")] public Expected? Expected { get; set; }

    public override string ToString() => Id;
}

/// <summary>Expected properties for an <c>accept</c> fixture.</summary>
public sealed class Expected
{
    [JsonPropertyName("streams")] public List<Dictionary<string, JsonElement>>? Streams { get; set; }
    [JsonPropertyName("duration_range")] public List<double>? DurationRange { get; set; }
    [JsonPropertyName("mp4_layout")] public string? Mp4Layout { get; set; }
}

[JsonSerializable(typeof(Manifest))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class ManifestJsonContext : JsonSerializerContext;
