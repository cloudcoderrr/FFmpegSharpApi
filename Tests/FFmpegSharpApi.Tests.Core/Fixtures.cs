namespace FFmpegSharpApi.Tests.Core;

/// <summary>Loads and caches the fixture manifest and exposes xUnit data sources.</summary>
public static class Fixtures
{
    private static readonly Lazy<Manifest> LazyManifest = new(() => Manifest.Load(TestPaths.ManifestPath));

    public static Manifest Manifest => LazyManifest.Value;

    public static Fixture ById(string id) => Manifest.Fixtures.First(f => f.Id == id);

    /// <summary>All fixture ids, one per xUnit theory case.</summary>
    public static IEnumerable<object[]> Ids() => Manifest.Fixtures.Select(f => new object[] { f.Id });

    /// <summary>Fixture ids carrying a given tag.</summary>
    public static IEnumerable<object[]> IdsWithTag(string tag)
        => Manifest.Fixtures.Where(f => f.Tags.Contains(tag)).Select(f => new object[] { f.Id });

    /// <summary>Accept-fixture ids whose path is under a folder prefix (e.g. <c>video/</c>).</summary>
    public static IEnumerable<object[]> IdsUnderFolder(string prefix)
        => Manifest.Fixtures
            .Where(f => f.Outcome == "accept" && f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f => new object[] { f.Id });
}
