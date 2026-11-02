namespace FFmpegSharpApi.Tests.Core;

/// <summary>
/// Captures FFmpeg log output for a single test to its own file under the logs directory,
/// and keeps the entries in memory for assertions.
/// </summary>
public sealed class TestLogScope : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Action<FFmpegLogEntry> _handler;
    private readonly List<FFmpegLogEntry> _entries = new();

    public TestLogScope(string id)
    {
        LogFile = Path.Combine(TestPaths.LogsDir, Sanitize(id) + ".log");
        _writer = new StreamWriter(LogFile, append: false) { AutoFlush = true };
        _writer.WriteLine($"# {id} @ {DateTime.UtcNow:O}");
        _handler = OnLog;
        FFmpegLog.MessageLogged += _handler;
    }

    /// <summary>Path of the per-test log file.</summary>
    public string LogFile { get; }

    /// <summary>Captured log entries.</summary>
    public IReadOnlyList<FFmpegLogEntry> Entries
    {
        get { lock (_entries) { return _entries.ToArray(); } }
    }

    /// <summary>Whether any captured message contains <paramref name="text"/>.</summary>
    public bool Contains(string text)
    {
        lock (_entries)
            return _entries.Any(e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Writes an explanatory note into the per-test log.</summary>
    public void Note(string text)
    {
        lock (_entries)
            _writer.WriteLine($"[NOTE   ] {text}");
    }

    private void OnLog(FFmpegLogEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
            _writer.WriteLine($"[{entry.Level,-7}] {entry.Message}");
        }
    }

    public void Dispose()
    {
        FFmpegLog.MessageLogged -= _handler;
        _writer.Dispose();
    }

    private static string Sanitize(string id)
        => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
