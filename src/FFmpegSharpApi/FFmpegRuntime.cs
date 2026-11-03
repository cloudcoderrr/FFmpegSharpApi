namespace FFmpegSharpApi;

/// <summary>
/// Entry point for initializing FFmpeg in a .NET application. Call
/// <see cref="Initialize(FFmpegRuntimeOptions?)"/> once at startup before invoking any
/// FFmpeg API. The correct native binding strategy is selected automatically for the
/// current platform: shared libraries are loaded dynamically on Windows, Linux, macOS and
/// Android, while iOS and Mac Catalyst use the statically linked FFmpeg compiled into the app.
/// </summary>
public static class FFmpegRuntime
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>Whether <see cref="Initialize(FFmpegRuntimeOptions?)"/> has completed.</summary>
    public static bool IsInitialized
    {
        get { lock (Gate) { return _initialized; } }
    }

    /// <summary>The directory the native libraries were loaded from, when applicable.</summary>
    public static string? NativeLibrariesPath { get; private set; }

    /// <summary>
    /// Initializes the FFmpeg bindings. Safe to call multiple times; the native bindings are
    /// bound once, while logging options are applied on every call that requests them.
    /// </summary>
    /// <param name="options">Optional configuration; defaults are used when omitted.</param>
    public static void Initialize(FFmpegRuntimeOptions? options = null)
    {
        options ??= new FFmpegRuntimeOptions();

        lock (Gate)
        {
            if (!_initialized)
            {
#if IOS || MACCATALYST
                FFmpeg.AutoGen.Bindings.StaticallyLinked.StaticallyLinkedBindings.Initialize();
#else
                var path = string.IsNullOrEmpty(options.NativeLibrariesPath)
                    ? NativeLibraryLocator.Resolve()
                    : options.NativeLibrariesPath!;
                NativeLibrariesPath = path;

                FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings.LibrariesPath = path;
                FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings.FunctionResolver =
                    new FFmpegFunctionResolver();
                FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings.Initialize();
#endif

                _initialized = true;
            }

            // Logging can be turned on by any caller, even after the one-time bind above
            // (for example when a host pre-initializes with logging disabled).
            if (options.EnableLogging)
                FFmpegLog.Enable(options.LogLevel, options.LogFilePath, options.LogToConsole);
        }
    }
}
