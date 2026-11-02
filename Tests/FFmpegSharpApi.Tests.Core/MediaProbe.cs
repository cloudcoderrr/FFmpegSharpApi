using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>A probed media stream.</summary>
public sealed class ProbeStream
{
    public string CodecType { get; set; } = string.Empty;
    public string CodecName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string? PixFmt { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? Profile { get; set; }
    public int HasBFrames { get; set; }
    public string RFrameRate { get; set; } = "0/0";
    public long BitRate { get; set; }
}

/// <summary>The result of probing a media file.</summary>
public sealed class ProbeResult
{
    public bool Success { get; set; }
    public int ErrorCode { get; set; }
    public string FormatName { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<ProbeStream> Streams { get; } = new();

    public ProbeStream? FirstVideo => Streams.FirstOrDefault(s => s.CodecType == "video");
    public ProbeStream? FirstAudio => Streams.FirstOrDefault(s => s.CodecType == "audio");
}

/// <summary>Opens and inspects media files through the FFmpeg demuxers.</summary>
public static unsafe class MediaProbe
{
    private const int ProfileUnknown = -99;

    /// <summary>Opens <paramref name="path"/> and reads stream/format metadata.</summary>
    public static ProbeResult Probe(string path, IReadOnlyList<string>? inputArgs = null)
    {
        var result = new ProbeResult();
        AVFormatContext* fmt = null;
        AVDictionary* options = null;
        AVInputFormat* inputFormat = Interop.ParseInputArgs(inputArgs, ref options);

        try
        {
            var open = ffmpeg.avformat_open_input(&fmt, path, inputFormat, &options);
            if (open < 0)
            {
                result.ErrorCode = open;
                return result;
            }

            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0)
            {
                result.ErrorCode = -1;
                return result;
            }

            if (fmt->iformat != null)
                result.FormatName = Interop.PtrToString(fmt->iformat->name);
            if (fmt->duration > 0)
                result.Duration = fmt->duration / (double)ffmpeg.AV_TIME_BASE;

            for (var i = 0u; i < fmt->nb_streams; i++)
            {
                var stream = fmt->streams[i];
                var codecpar = stream->codecpar;
                var probe = new ProbeStream
                {
                    CodecType = MediaType(codecpar->codec_type),
                    CodecName = ffmpeg.avcodec_get_name(codecpar->codec_id),
                    Width = codecpar->width,
                    Height = codecpar->height,
                    SampleRate = codecpar->sample_rate,
                    Channels = codecpar->ch_layout.nb_channels,
                    HasBFrames = codecpar->video_delay,
                    BitRate = codecpar->bit_rate,
                    RFrameRate = $"{stream->r_frame_rate.num}/{stream->r_frame_rate.den}",
                };

                if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && codecpar->format >= 0)
                    probe.PixFmt = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)codecpar->format);

                if (codecpar->profile != ProfileUnknown)
                    probe.Profile = ffmpeg.avcodec_profile_name(codecpar->codec_id, codecpar->profile);

                result.Streams.Add(probe);
            }

            result.Success = true;
            return result;
        }
        finally
        {
            if (options != null)
                ffmpeg.av_dict_free(&options);
            if (fmt != null)
                ffmpeg.avformat_close_input(&fmt);
        }
    }

    private static string MediaType(AVMediaType type) => type switch
    {
        AVMediaType.AVMEDIA_TYPE_VIDEO => "video",
        AVMediaType.AVMEDIA_TYPE_AUDIO => "audio",
        AVMediaType.AVMEDIA_TYPE_SUBTITLE => "subtitle",
        AVMediaType.AVMEDIA_TYPE_DATA => "data",
        AVMediaType.AVMEDIA_TYPE_ATTACHMENT => "attachment",
        _ => "unknown",
    };
}
