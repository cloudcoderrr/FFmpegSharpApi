using FFmpeg.AutoGen.Abstractions;

namespace FFmpegSharpApi.Tests.Core;

/// <summary>Outcome of decoding a media stream.</summary>
public sealed class DecodeResult
{
    public bool Opened { get; set; }
    public bool Success { get; set; }
    public bool NoDecoder { get; set; }
    public int FramesDecoded { get; set; }
    public int ErrorCode { get; set; }
    public string Error => Interop.ErrorString(ErrorCode);
}

/// <summary>Decodes media and extracts verification artifacts (snapshots and audio snippets).</summary>
public static unsafe class MediaDecoder
{
    private const int SwsBilinear = 2;

    /// <summary>Diagnostic describing why the last snapshot attempt failed.</summary>
    public static string LastSnapshotError = string.Empty;

    /// <summary>Decodes up to <paramref name="maxFrames"/> frames of the best stream of a type.</summary>
    public static DecodeResult Decode(string path, IReadOnlyList<string>? inputArgs, AVMediaType type, int maxFrames)
    {
        var result = new DecodeResult();
        AVFormatContext* fmt = null;
        AVDictionary* opts = null;
        var inputFormat = Interop.ParseInputArgs(inputArgs, ref opts);
        AVCodecContext* dec = null;
        AVPacket* packet = null;
        AVFrame* frame = null;

        try
        {
            var open = ffmpeg.avformat_open_input(&fmt, path, inputFormat, &opts);
            if (open < 0) { result.ErrorCode = open; return result; }
            result.Opened = true;

            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) { result.ErrorCode = -1; return result; }

            AVCodec* codec = null;
            var index = ffmpeg.av_find_best_stream(fmt, type, -1, -1, &codec, 0);
            if (index == ffmpeg.AVERROR_DECODER_NOT_FOUND || (index >= 0 && codec == null))
            {
                result.NoDecoder = true;
                result.ErrorCode = index;
                return result;
            }
            if (index < 0) { result.ErrorCode = index; return result; }

            dec = ffmpeg.avcodec_alloc_context3(codec);
            if (ffmpeg.avcodec_parameters_to_context(dec, fmt->streams[index]->codecpar) < 0) { result.ErrorCode = -1; return result; }
            if (ffmpeg.avcodec_open2(dec, codec, null) < 0) { result.ErrorCode = -1; return result; }

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();

            while (result.FramesDecoded < maxFrames && ffmpeg.av_read_frame(fmt, packet) >= 0)
            {
                if (packet->stream_index == index && ffmpeg.avcodec_send_packet(dec, packet) == 0)
                {
                    while (ffmpeg.avcodec_receive_frame(dec, frame) == 0)
                    {
                        result.FramesDecoded++;
                        ffmpeg.av_frame_unref(frame);
                        if (result.FramesDecoded >= maxFrames) break;
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }

            // Flush the decoder to emit buffered frames (single-frame and delayed streams).
            if (result.FramesDecoded < maxFrames)
            {
                ffmpeg.avcodec_send_packet(dec, null);
                while (ffmpeg.avcodec_receive_frame(dec, frame) == 0)
                {
                    result.FramesDecoded++;
                    ffmpeg.av_frame_unref(frame);
                    if (result.FramesDecoded >= maxFrames) break;
                }
            }

            result.Success = result.FramesDecoded > 0;
            return result;
        }
        finally
        {
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (dec != null) ffmpeg.avcodec_free_context(&dec);
            if (opts != null) ffmpeg.av_dict_free(&opts);
            if (fmt != null) ffmpeg.avformat_close_input(&fmt);
        }
    }

    /// <summary>Decodes a single video frame to obtain the real geometry (for still images etc.).</summary>
    public static bool TryGetVideoGeometry(string path, IReadOnlyList<string>? inputArgs, out int width, out int height, out string? pixFmt)
    {
        width = 0;
        height = 0;
        pixFmt = null;
        AVFormatContext* fmt = null;
        AVDictionary* opts = null;
        var inputFormat = Interop.ParseInputArgs(inputArgs, ref opts);
        AVCodecContext* dec = null;
        AVPacket* packet = null;
        AVFrame* frame = null;

        try
        {
            if (ffmpeg.avformat_open_input(&fmt, path, inputFormat, &opts) < 0) return false;
            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) return false;

            AVCodec* codec = null;
            var index = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
            if (index < 0 || codec == null) return false;

            dec = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(dec, fmt->streams[index]->codecpar);
            if (ffmpeg.avcodec_open2(dec, codec, null) < 0) return false;

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            var got = false;
            while (!got && ffmpeg.av_read_frame(fmt, packet) >= 0)
            {
                if (packet->stream_index == index && ffmpeg.avcodec_send_packet(dec, packet) == 0)
                    got = ffmpeg.avcodec_receive_frame(dec, frame) == 0;
                ffmpeg.av_packet_unref(packet);
            }
            if (!got)
            {
                ffmpeg.avcodec_send_packet(dec, null);
                got = ffmpeg.avcodec_receive_frame(dec, frame) == 0;
            }
            if (!got) return false;

            width = frame->width;
            height = frame->height;
            pixFmt = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)frame->format);
            return true;
        }
        finally
        {
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (dec != null) ffmpeg.avcodec_free_context(&dec);
            if (opts != null) ffmpeg.av_dict_free(&opts);
            if (fmt != null) ffmpeg.avformat_close_input(&fmt);
        }
    }

    /// <summary>Decodes the first video frame and writes it as a PNG snapshot.</summary>
    public static bool SaveVideoSnapshot(string path, IReadOnlyList<string>? inputArgs, string outputPng, int maxWidth = 640)
    {
        AVFormatContext* fmt = null;
        AVDictionary* opts = null;
        var inputFormat = Interop.ParseInputArgs(inputArgs, ref opts);
        AVCodecContext* dec = null;
        AVCodecContext* enc = null;
        AVPacket* packet = null;
        AVPacket* outPacket = null;
        AVFrame* frame = null;
        AVFrame* rgb = null;
        SwsContext* sws = null;

        try
        {
            LastSnapshotError = string.Empty;
            if (ffmpeg.avformat_open_input(&fmt, path, inputFormat, &opts) < 0) { LastSnapshotError = "open"; return false; }
            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) { LastSnapshotError = "stream_info"; return false; }

            AVCodec* codec = null;
            var index = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
            if (index < 0 || codec == null) { LastSnapshotError = "no_stream"; return false; }

            dec = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(dec, fmt->streams[index]->codecpar);
            if (ffmpeg.avcodec_open2(dec, codec, null) < 0) { LastSnapshotError = "dec_open"; return false; }

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            var got = false;
            while (!got && ffmpeg.av_read_frame(fmt, packet) >= 0)
            {
                if (packet->stream_index == index && ffmpeg.avcodec_send_packet(dec, packet) == 0)
                    got = ffmpeg.avcodec_receive_frame(dec, frame) == 0;
                ffmpeg.av_packet_unref(packet);
            }
            if (!got) { LastSnapshotError = "no_frame"; return false; }

            int sw = frame->width, sh = frame->height;
            int dw = sw, dh = sh;
            if (dw > maxWidth)
            {
                dh = (int)((long)sh * maxWidth / sw);
                dw = maxWidth;
                if ((dh & 1) == 1) dh++;
                if (dh < 1) dh = 1;
            }

            rgb = ffmpeg.av_frame_alloc();
            rgb->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
            rgb->width = dw;
            rgb->height = dh;
            if (ffmpeg.av_frame_get_buffer(rgb, 32) < 0) { LastSnapshotError = "get_buffer"; return false; }

            sws = ffmpeg.sws_getContext(sw, sh, (AVPixelFormat)frame->format, dw, dh,
                AVPixelFormat.AV_PIX_FMT_RGB24, SwsBilinear, null, null, null);
            if (sws == null) { LastSnapshotError = "sws"; return false; }
            ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, sh, rgb->data, rgb->linesize);

            var pngCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_PNG);
            if (pngCodec == null) { LastSnapshotError = "no_png_encoder"; return false; }
            enc = ffmpeg.avcodec_alloc_context3(pngCodec);
            enc->width = dw;
            enc->height = dh;
            enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_RGB24;
            enc->time_base = new AVRational { num = 1, den = 1 };
            if (ffmpeg.avcodec_open2(enc, pngCodec, null) < 0) { LastSnapshotError = "png_open"; return false; }

            outPacket = ffmpeg.av_packet_alloc();
            rgb->pts = 0;
            var sendResult = ffmpeg.avcodec_send_frame(enc, rgb);
            if (sendResult < 0) { LastSnapshotError = "png_send:" + Interop.ErrorString(sendResult); return false; }
            ffmpeg.avcodec_send_frame(enc, null); // flush so the single frame is emitted
            var recvResult = ffmpeg.avcodec_receive_packet(enc, outPacket);
            if (recvResult < 0) { LastSnapshotError = "png_receive:" + Interop.ErrorString(recvResult); return false; }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPng))!);
            using var file = File.Create(outputPng);
            file.Write(new ReadOnlySpan<byte>(outPacket->data, outPacket->size));
            return true;
        }
        finally
        {
            if (sws != null) ffmpeg.sws_freeContext(sws);
            if (outPacket != null) ffmpeg.av_packet_free(&outPacket);
            if (rgb != null) ffmpeg.av_frame_free(&rgb);
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (enc != null) ffmpeg.avcodec_free_context(&enc);
            if (dec != null) ffmpeg.avcodec_free_context(&dec);
            if (opts != null) ffmpeg.av_dict_free(&opts);
            if (fmt != null) ffmpeg.avformat_close_input(&fmt);
        }
    }

    /// <summary>Decodes up to <paramref name="seconds"/> of audio and writes a 16-bit stereo WAV.</summary>
    public static bool SaveAudioSnippet(string path, IReadOnlyList<string>? inputArgs, string outputWav, double seconds = 2.0)
    {
        AVFormatContext* fmt = null;
        AVDictionary* opts = null;
        var inputFormat = Interop.ParseInputArgs(inputArgs, ref opts);
        AVCodecContext* dec = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        SwrContext* swr = null;
        AVChannelLayout outLayout = default;

        try
        {
            if (ffmpeg.avformat_open_input(&fmt, path, inputFormat, &opts) < 0) return false;
            if (ffmpeg.avformat_find_stream_info(fmt, null) < 0) return false;

            AVCodec* codec = null;
            var index = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);
            if (index < 0 || codec == null) return false;

            dec = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_parameters_to_context(dec, fmt->streams[index]->codecpar);
            if (ffmpeg.avcodec_open2(dec, codec, null) < 0) return false;

            const int outChannels = 2;
            var outRate = dec->sample_rate > 0 ? dec->sample_rate : 44100;
            ffmpeg.av_channel_layout_default(&outLayout, outChannels);

            if (ffmpeg.swr_alloc_set_opts2(&swr, &outLayout, AVSampleFormat.AV_SAMPLE_FMT_S16, outRate,
                    &dec->ch_layout, dec->sample_fmt, dec->sample_rate, 0, null) < 0)
                return false;
            if (ffmpeg.swr_init(swr) < 0) return false;

            packet = ffmpeg.av_packet_alloc();
            frame = ffmpeg.av_frame_alloc();
            var planes = stackalloc byte*[1];
            using var pcm = new MemoryStream();
            var maxSamples = (long)(outRate * seconds);
            long written = 0;

            while (written < maxSamples && ffmpeg.av_read_frame(fmt, packet) >= 0)
            {
                if (packet->stream_index == index && ffmpeg.avcodec_send_packet(dec, packet) == 0)
                {
                    while (ffmpeg.avcodec_receive_frame(dec, frame) == 0)
                    {
                        var outSamples = (int)ffmpeg.av_rescale_rnd(
                            ffmpeg.swr_get_delay(swr, dec->sample_rate) + frame->nb_samples,
                            outRate, dec->sample_rate, AVRounding.AV_ROUND_UP);
                        var bufferSize = outSamples * outChannels * 2;
                        var outBuffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);
                        planes[0] = outBuffer;
                        var converted = ffmpeg.swr_convert(swr, planes, outSamples, frame->extended_data, frame->nb_samples);
                        if (converted > 0)
                        {
                            pcm.Write(new ReadOnlySpan<byte>(outBuffer, converted * outChannels * 2));
                            written += converted;
                        }
                        ffmpeg.av_free(outBuffer);
                        ffmpeg.av_frame_unref(frame);
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }

            WriteWav(outputWav, pcm.ToArray(), outRate, outChannels);
            return pcm.Length > 0;
        }
        finally
        {
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (swr != null) ffmpeg.swr_free(&swr);
            ffmpeg.av_channel_layout_uninit(&outLayout);
            if (dec != null) ffmpeg.avcodec_free_context(&dec);
            if (opts != null) ffmpeg.av_dict_free(&opts);
            if (fmt != null) ffmpeg.avformat_close_input(&fmt);
        }
    }

    private static void WriteWav(string path, byte[] pcm, int sampleRate, int channels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path));
        var byteRate = sampleRate * channels * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);              // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * 2)); // block align
        writer.Write((short)16);             // bits per sample
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
