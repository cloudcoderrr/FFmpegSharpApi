# FFmpegSharpApi

Cross-platform **FFmpeg 9.0** bindings for .NET. Call the FFmpeg C APIs directly from C#
on **Windows, Linux, macOS, Mac Catalyst, iOS and Android**, from **.NET MAUI, Avalonia,
WinUI 3 and Win32** applications.

The managed API is built on [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
(MIT). The native libraries are produced from upstream FFmpeg and shipped in companion
`FFmpegSharpApi.Native.*` packages. Both an **LGPL v3** (default) and a **GPL v3** build
flavour are available.

## Quick start

```csharp
using FFmpegSharpApi;
using FFmpeg.AutoGen.Abstractions;

// Initialize once at startup. Picks the right native loader for the platform
// and (optionally) routes FFmpeg logs to a file.
FFmpegRuntime.Initialize(new FFmpegRuntimeOptions
{
    EnableLogging = true,
    LogLevel = FFmpegLogLevel.Verbose,
    LogFilePath = "ffmpeg.log",
});

Console.WriteLine(FFmpegInfo.VersionInfo);   // 9.0.1
Console.WriteLine(FFmpegInfo.License);       // LGPL version 3 or later
Console.WriteLine(FFmpegInfo.Configuration); // ./configure flags

// From here use the FFmpeg API through the `ffmpeg` static class, e.g.
unsafe
{
    AVFormatContext* ctx = null;
    if (ffmpeg.avformat_open_input(&ctx, "input.mp4", null, null) == 0)
    {
        ffmpeg.avformat_find_stream_info(ctx, null);
        ffmpeg.av_dump_format(ctx, 0, "input.mp4", 0);
        ffmpeg.avformat_close_input(&ctx);
    }
}
```

## Native libraries

Reference the native asset package(s) for the platforms you target, or the
`FFmpegSharpApi.Native` meta-package for all of them:

| Package | Runtimes |
| --- | --- |
| `FFmpegSharpApi.Native.Win32` | win-x64, win-x86, win-arm64 |
| `FFmpegSharpApi.Native.Linux` | linux-x64, linux-arm64, linux-arm, linux-x86 |
| `FFmpegSharpApi.Native.macOS` | osx-x64, osx-arm64 |
| `FFmpegSharpApi.Native.Android` | android arm64-v8a, armeabi-v7a, x86_64, x86 |
| `FFmpegSharpApi.Native.iOS` | ios device + simulator (static xcframework) |
| `FFmpegSharpApi.Native.MacCatalyst` | maccatalyst x64 + arm64 (static xcframework) |

On iOS and Mac Catalyst FFmpeg is statically linked into your app; on every other platform
the shared libraries are loaded at runtime.

## Logging

All FFmpeg log output can be captured through `FFmpegLog`:

```csharp
FFmpegLog.MessageLogged += e => Debug.WriteLine($"{e.Level}: {e.Message}");
FFmpegLog.Enable(FFmpegLogLevel.Debug, filePath: "ffmpeg.log", toConsole: true);
```

## License

The managed bindings are MIT licensed. The native FFmpeg libraries are distributed under
LGPL v3 (default) or GPL v3 depending on the `FFmpegSharpApi.Native.*` variant you choose.
See [FFmpeg legal](https://www.ffmpeg.org/legal.html).
