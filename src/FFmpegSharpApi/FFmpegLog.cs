using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi;

/// <summary>A single FFmpeg log line delivered to managed sinks.</summary>
/// <param name="Level">The severity of the message.</param>
/// <param name="Message">The formatted message text, without a trailing newline.</param>
public readonly record struct FFmpegLogEntry(FFmpegLogLevel Level, string Message);

/// <summary>
/// Bridges the native FFmpeg log callback to managed consumers. Log lines can be written
/// to a file, echoed to the console, and observed through <see cref="MessageLogged"/>.
/// </summary>
public static unsafe class FFmpegLog
{
    private static readonly object Gate = new();
    private static av_log_set_callback_callback? _callback;
    private static TextWriter? _file;
    private static bool _toConsole;

    /// <summary>Raised for every log line at or below the configured level.</summary>
    public static event Action<FFmpegLogEntry>? MessageLogged;

    /// <summary>The level currently applied to the native logger.</summary>
    public static FFmpegLogLevel Level { get; private set; } = FFmpegLogLevel.Info;

    /// <summary>
    /// Installs the managed log callback and sets the FFmpeg log level.
    /// </summary>
    /// <param name="level">Maximum verbosity to capture.</param>
    /// <param name="filePath">Optional file that receives every captured line.</param>
    /// <param name="toConsole">Whether to echo captured lines to the console.</param>
    public static void Enable(FFmpegLogLevel level, string? filePath = null, bool toConsole = false)
    {
        lock (Gate)
        {
            Level = level;
            ffmpeg.av_log_set_level((int)level);

            if (!string.IsNullOrEmpty(filePath))
            {
                var full = Path.GetFullPath(filePath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                _file?.Dispose();
                _file = new StreamWriter(full, append: false) { AutoFlush = true };
            }

            _toConsole = toConsole;
            _callback = Callback;
            ffmpeg.av_log_set_callback(_callback);
        }
    }

    /// <summary>Restores the default FFmpeg log callback and closes the log file.</summary>
    public static void Disable()
    {
        lock (Gate)
        {
            av_log_set_callback_callback @default = ffmpeg.av_log_default_callback;
            ffmpeg.av_log_set_callback(@default);
            _callback = @default;
            _file?.Dispose();
            _file = null;
        }
    }

    private static void Callback(void* avcl, int level, string format, byte* vl)
    {
        if (level > ffmpeg.av_log_get_level())
            return;

        const int bufferSize = 4096;
        var buffer = stackalloc byte[bufferSize];
        var printPrefix = 1;
        ffmpeg.av_log_format_line2(avcl, level, format, vl, buffer, bufferSize, &printPrefix);

        var message = (Marshal.PtrToStringAnsi((IntPtr)buffer) ?? string.Empty)
            .TrimEnd('\r', '\n');
        var entry = new FFmpegLogEntry((FFmpegLogLevel)level, message);

        lock (Gate)
        {
            _file?.WriteLine($"{DateTime.UtcNow:O} [{entry.Level,-7}] {entry.Message}");
            if (_toConsole)
                Console.WriteLine($"[ffmpeg {entry.Level,-7}] {entry.Message}");
        }

        MessageLogged?.Invoke(entry);
    }
}
