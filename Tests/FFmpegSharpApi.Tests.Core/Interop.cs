using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>Shared helpers for the FFmpeg interop used by the tests.</summary>
internal static unsafe class Interop
{
    /// <summary>
    /// Parses manifest <c>input_args</c> (e.g. <c>-f rawvideo -pixel_format yuv420p
    /// -video_size 160x90 -framerate 12</c>) into a forced input format and options dictionary.
    /// </summary>
    public static AVInputFormat* ParseInputArgs(IReadOnlyList<string>? args, ref AVDictionary* options)
    {
        AVInputFormat* inputFormat = null;
        if (args is null)
            return inputFormat;

        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            var flag = args[i];
            var value = args[i + 1];
            if (flag == "-f")
            {
                inputFormat = ffmpeg.av_find_input_format(value);
            }
            else
            {
                var key = flag.TrimStart('-');
                fixed (AVDictionary** p = &options)
                    ffmpeg.av_dict_set(p, key, value, 0);
            }
        }

        return inputFormat;
    }

    public static string PtrToString(byte* p) => Marshal.PtrToStringAnsi((IntPtr)p) ?? string.Empty;

    /// <summary>Formats an FFmpeg error code as a readable message.</summary>
    public static string ErrorString(int error)
    {
        const int size = 1024;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(error, buffer, size);
        return PtrToString(buffer);
    }
}
