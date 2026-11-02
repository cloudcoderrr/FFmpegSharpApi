#if !IOS && !MACCATALYST
using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace FFmpegSharpApi;

/// <summary>
/// Resolves FFmpeg shared libraries across desktop and Android. It understands the
/// platform-specific file naming (versioned on Windows/Linux/macOS, unversioned on
/// Android) and falls back to the bare soname so the OS loader can search its default
/// paths (required for Android's packaged <c>lib*.so</c> files).
/// </summary>
internal sealed class FFmpegFunctionResolver : FunctionResolverBase
{
    protected override string GetNativeLibraryName(string libraryName, int version)
    {
        if (OperatingSystem.IsWindows())
            return $"{libraryName}-{version}.dll";
        if (OperatingSystem.IsMacOS())
            return $"lib{libraryName}.{version}.dylib";
        if (OperatingSystem.IsAndroid())
            return $"lib{libraryName}.so"; // Android disallows versioned sonames in an APK.
        return $"lib{libraryName}.so.{version}";
    }

    protected override IntPtr LoadNativeLibrary(string path)
    {
        // 1) The computed (versioned) path.
        if (NativeLibrary.TryLoad(path, out var handle))
            return handle;

        // 2) The bare file name so the OS loader searches its default directories
        //    (required for Android's packaged lib*.so files).
        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName) && NativeLibrary.TryLoad(fileName, out handle))
            return handle;

        // 3) The unversioned soname (symlink on Linux/macOS) to tolerate version drift.
        var unversioned = StripVersion(path);
        if (!string.Equals(unversioned, path, StringComparison.Ordinal))
        {
            if (NativeLibrary.TryLoad(unversioned, out handle))
                return handle;
            var unversionedName = Path.GetFileName(unversioned);
            if (!string.IsNullOrEmpty(unversionedName) && NativeLibrary.TryLoad(unversionedName, out handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    // libavcodec.so.63 -> libavcodec.so ; libavcodec.63.dylib -> libavcodec.dylib ;
    // avcodec-63.dll -> avcodec.dll
    private static string StripVersion(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);

        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dash = name.LastIndexOf('-');
            if (dash > 0)
                name = string.Concat(name.AsSpan(0, dash), ".dll");
        }
        else if (name.Contains(".so.", StringComparison.Ordinal))
        {
            name = name[..(name.IndexOf(".so.", StringComparison.Ordinal) + 3)];
        }
        else if (name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
        {
            var firstDot = name.IndexOf('.');
            var dylibIdx = name.LastIndexOf(".dylib", StringComparison.OrdinalIgnoreCase);
            if (firstDot > 0 && dylibIdx > firstDot)
                name = string.Concat(name.AsSpan(0, firstDot), ".dylib");
        }

        return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
    }

    protected override IntPtr GetFunctionPointer(IntPtr nativeLibraryHandle, string functionName)
        => NativeLibrary.TryGetExport(nativeLibraryHandle, functionName, out var address)
            ? address
            : IntPtr.Zero;
}
#endif
