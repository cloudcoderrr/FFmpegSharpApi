#!/usr/bin/env python3
"""Generate the synthetic FFmpeg fixture pack. Python standard library only."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import random
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from urllib.parse import quote

ROOT = Path(__file__).resolve().parent


def sha256(path: Path) -> str:
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', default='ffmpeg', help='Reference encoder with the codecs listed in README.md')
    args = parser.parse_args()
    binary = shutil.which(args.ffmpeg) or args.ffmpeg
    version = subprocess.check_output([binary, '-version'], text=True, encoding='utf-8', errors='replace')
    encoders = subprocess.check_output([binary, '-hide_banner', '-encoders'], text=True, encoding='utf-8', errors='replace')
    required = ['libx264', 'libx264rgb', 'libx265', 'libvpx', 'libvpx-vp9', 'libaom-av1', 'libmp3lame',
                'libopus', 'libvorbis', 'libtheora', 'libwebp', 'libopencore_amrnb']
    available = {line.split()[1] for line in encoders.splitlines() if len(line.split()) >= 2}
    missing = sorted(set(required) - available)
    if missing:
        parser.error('Reference encoder is missing: ' + ', '.join(missing) + '. Pass --ffmpeg with a full build; existing fixtures can be tested with a smaller build.')

    fixtures: list[dict] = []
    assets: dict[str, str] = {}
    commands: list[dict] = []

    def location(name: str) -> Path:
        path = ROOT / name
        path.parent.mkdir(parents=True, exist_ok=True)
        return path

    def resource(name: str, content: str | bytes, role: str = 'sidecar') -> Path:
        path = location(name)
        path.write_bytes(content.encode('utf-8') if isinstance(content, str) else content)
        assets[name] = role
        return path

    def portable(value: str) -> str:
        return str(value).replace(str(ROOT), '${ROOT}')

    def run(arguments: list[str], purpose: str) -> None:
        argv = [str(v) for v in arguments]
        command = [binary, '-hide_banner', '-nostdin', '-y', '-v', 'error', '-threads', '2', '-filter_threads', '1', *argv]
        result = subprocess.run(command, cwd=Path(argv[-1]).parent, capture_output=True, encoding='utf-8', errors='replace', timeout=180)
        commands.append({'purpose': purpose, 'argv': ['${FFMPEG}', *[portable(v) for v in command[1:]]]})
        if result.returncode:
            raise RuntimeError(f'{purpose}: exit {result.returncode}\n{result.stderr[-8000:]}')

    def add(name: str, scenario: str, expected: dict, *, outcome: str = 'accept', input_args: list[str] | None = None, tags: list[str] | None = None) -> None:
        assets[name] = 'fixture'
        fixtures.append({'id': name.rsplit('.', 1)[0].replace('/', '.'), 'path': name,
                         'scenario': scenario, 'tags': tags or [name.split('/')[0]],
                         'outcome': outcome, 'input_args': input_args or [], 'expected': expected})
        print(f'[{len(fixtures):03d}] {name}', flush=True)

    def make(name: str, arguments: list[str], scenario: str, expected: dict, **kwargs) -> None:
        run([*arguments, str(location(name))], name)
        add(name, scenario, expected, **kwargs)

    def video(codec: str = 'h264', width: int = 320, height: int = 180, **other) -> dict:
        return {'codec_type': 'video', 'codec_name': codec, 'width': width, 'height': height, **other}

    def audio(codec: str, rate: int = 48000, channels: int = 2, **other) -> dict:
        return {'codec_type': 'audio', 'codec_name': codec, 'sample_rate': str(rate), 'channels': channels, **other}

    def expected(*streams: dict, duration: tuple[float, float] | None = None, **other) -> dict:
        return {'streams': list(streams), **({'duration_range': list(duration)} if duration else {}), **other}

    def source(size: str = '320x180', rate: str = '24', duration: str = '2') -> list[str]:
        return ['-f', 'lavfi', '-i', f'testsrc2=size={size}:rate={rate}:duration={duration}']

    def tone(rate: int = 48000, duration: str = '2', channels: int = 2) -> list[str]:
        expressions = '|'.join(f'0.15*sin(2*PI*{440 + i * 220}*t)' for i in range(channels))
        return ['-f', 'lavfi', '-i', f'aevalsrc={expressions}:s={rate}:d={duration}']

    h264 = ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '28', '-pix_fmt', 'yuv420p', '-threads:v', '2']
    h265 = ['-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '32', '-x265-params', 'pools=1:frame-threads=1:log-level=error']
    aac = ['-c:a', 'aac', '-b:a', '96k']
    base = 'video/h264_aac_stereo_faststart.mp4'
    basepath = str(location(base))
    make(base, [*source('640x360', duration='3'), *tone(duration='3'), *h264, '-g', '24', *aac, '-movflags', '+faststart'],
         'Everyday MP4: H.264 video, stereo AAC, seekable one-second GOPs, moov before mdat.',
         expected(video(width=640, height=360, pix_fmt='yuv420p'), audio('aac'), duration=(2.95, 3.1), mp4_layout='faststart'))
    make('video/h264_baseline_video_only.mp4', [*source(), *h264, '-profile:v', 'baseline', '-bf', '0', '-g', '24', '-an'],
         'Video-only baseline profile, no B frames; handles a missing audio stream.', expected(video(profile='Constrained Baseline', has_b_frames=0), duration=(1.95, 2.1)))
    make('video/h264_high_bframes_ntsc.mp4', [*source(rate='30000/1001'), *h264, '-profile:v', 'high', '-bf', '3', '-g', '30'],
         'NTSC fractional frame rate and reordered B-frame timestamps.', expected(video(profile='High', r_frame_rate='30000/1001', min_b_frames=1), duration=(1.95, 2.15)))
    make('video/hevc_main_aac.mp4', [*source(), *tone(), *h265, '-pix_fmt', 'yuv420p', '-tag:v', 'hvc1', *aac],
         'HEVC Main profile in an Apple-compatible hvc1 MP4.', expected(video('hevc', pix_fmt='yuv420p'), audio('aac'), duration=(1.95, 2.15)))
    make('video/hevc_main10_hdr_tags.mkv', [*source(duration='1'), *h265, '-x265-params', 'pools=1:frame-threads=1:log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:hdr10=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400', '-pix_fmt', 'yuv420p10le', '-color_primaries', 'bt2020', '-color_trc', 'smpte2084', '-colorspace', 'bt2020nc', '-color_range', 'tv'],
         'Synthetic 10-bit HEVC carrying BT.2020/PQ tags for HDR metadata handling; not mastered HDR imagery.',
         expected(video('hevc', pix_fmt='yuv420p10le', color_space='bt2020nc'),
                  frame_properties={'pix_fmt': 'yuv420p10le', 'color_primaries': 'bt2020', 'color_transfer': 'smpte2084', 'color_space': 'bt2020nc'},
                  frame_side_data_types=['Mastering display metadata', 'Content light level metadata']))
    make('video/vp8_vorbis.webm', [*source(), *tone(), '-c:v', 'libvpx', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '250k', '-c:a', 'libvorbis', '-q:a', '3'],
         'Legacy browser WebM with VP8 and Vorbis.', expected(video('vp8'), audio('vorbis')))
    make('video/vp9_opus.webm', [*source(), *tone(), '-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '200k', '-c:a', 'libopus', '-b:a', '64k'],
         'Modern browser WebM with VP9 and Opus.', expected(video('vp9'), audio('opus')))
    make('video/vp9_10bit.mkv', [*source(duration='1'), '-c:v', 'libvpx-vp9', '-deadline', 'realtime', '-cpu-used', '8', '-b:v', '200k', '-pix_fmt', 'yuv420p10le'],
         'VP9 profile 2 with 10-bit pixels.', expected(video('vp9', profile='Profile 2', pix_fmt='yuv420p10le')))
    make('video/av1_aac.mp4', [*source(rate='12', duration='1'), *tone(duration='1'), '-c:v', 'libaom-av1', '-cpu-used', '8', '-row-mt', '1', '-crf', '40', '-b:v', '0', '-threads:v', '2', *aac],
         'AV1 in MP4; exercises software decoder availability.', expected(video('av1'), audio('aac'), duration=(0.95, 1.15)))
    make('video/mpeg4_mp3.avi', [*source(), *tone(rate=44100), '-c:v', 'mpeg4', '-q:v', '5', '-c:a', 'libmp3lame', '-b:a', '96k'],
         'MPEG-4 Part 2 and MP3 in AVI.', expected(video('mpeg4'), audio('mp3', 44100)))
    make('video/mpeg2_mp2.mpg', [*source('352x288', '25'), *tone(), '-c:v', 'mpeg2video', '-b:v', '600k', '-c:a', 'mp2', '-b:a', '128k', '-f', 'mpeg'],
         'MPEG program stream for older TV/DVD workflows.', expected(video('mpeg2video', 352, 288), audio('mp2')))
    make('video/wmv2_wma.asf', [*source(), *tone(rate=44100), '-c:v', 'wmv2', '-b:v', '300k', '-c:a', 'wmav2', '-b:a', '96k'],
         'Windows Media ASF playback and conversion.', expected(video('wmv2'), audio('wmav2', 44100)))
    make('video/flv_mp3.flv', [*source(), *tone(rate=44100), '-c:v', 'flv', '-b:v', '300k', '-c:a', 'libmp3lame', '-b:a', '96k'],
         'Legacy FLV video and MP3 (this muxer exposes audio before video).', expected(audio('mp3', 44100), video('flv1')))
    make('video/theora_vorbis.ogv', [*source(), *tone(), '-c:v', 'libtheora', '-q:v', '4', '-c:a', 'libvorbis', '-q:a', '3'],
         'Ogg container with Theora video and Vorbis audio.', expected(video('theora'), audio('vorbis')))
    make('video/mjpeg_pcm.avi', [*source(duration='1'), *tone(duration='1'), '-c:v', 'mjpeg', '-q:v', '5', '-pix_fmt', 'yuvj420p', '-c:a', 'pcm_s16le'],
         'Intra-frame MJPEG with uncompressed audio and full-range video.', expected(video('mjpeg', pix_fmt='yuvj420p', color_range='pc'), audio('pcm_s16le')))
    make('video/prores422_pcm.mov', [*source(duration='1'), *tone(duration='1'), '-c:v', 'prores_ks', '-profile:v', '2', '-pix_fmt', 'yuv422p10le', '-c:a', 'pcm_s24le'],
         'Editing intermediate: 10-bit ProRes 422 and 24-bit PCM.', expected(video('prores', pix_fmt='yuv422p10le'), audio('pcm_s24le')))
    make('video/prores4444_alpha.mov', ['-f', 'lavfi', '-i', 'color=c=red@0.5:s=160x90:r=12:d=1,format=yuva444p10le', '-c:v', 'prores_ks', '-profile:v', '4', '-alpha_bits', '16', '-pix_fmt', 'yuva444p10le'],
         'ProRes 4444 with a semi-transparent alpha plane.', expected(video('prores', 160, 90, pix_fmt='yuva444p12le'), alpha=True))
    make('video/ffv1_flac_lossless.mkv', [*source(duration='1'), *tone(duration='1'), '-c:v', 'ffv1', '-level', '3', '-pix_fmt', 'yuv444p10le', '-c:a', 'flac'],
         'Lossless archive with 10-bit 4:4:4 FFV1 and FLAC.', expected(video('ffv1', pix_fmt='yuv444p10le'), audio('flac')))
    make('video/mobile_h263_aac.3gp', [*source('176x144', '15', '1'), *tone(16000, '1', 1), '-c:v', 'h263', '-b:v', '128k', *aac],
         'Legacy mobile 3GP with H.263 video and mono AAC.', expected(video('h263', 176, 144), audio('aac', 16000, 1)))
    make('video/h264_high10.mp4', [*source(duration='1'), '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '28', '-pix_fmt', 'yuv420p10le', '-threads:v', '2'],
         'H.264 High 10 profile, often unsupported by hardware-only playback paths.', expected(video(profile='High 10', pix_fmt='yuv420p10le')))
    make('video/h264_rgb_screen_capture.mkv', ['-f', 'lavfi', '-i', 'testsrc=size=320x180:rate=12:duration=1', '-c:v', 'libx264rgb', '-preset', 'veryfast', '-crf', '0', '-threads:v', '2'],
         'Lossless RGB H.264 screen-capture style input with 4:4:4 pixels.', expected(video(pix_fmt='gbrp')))
    make('properties/long_gop_15s.mp4', [*source(rate='12', duration='15'), *h264, '-g', '120', '-keyint_min', '120', '-sc_threshold', '0', '-an'],
         'Fifteen-second video with ten-second GOPs for accurate seeking near the end.', expected(video(r_frame_rate='12/1'), duration=(14.95, 15.1)))

    # Dimensions, timing, color, display transforms and incomplete stream sets.
    for name, size, rate, duration in [('portrait_9x16', '180x320', '24', '1'), ('hd_720p', '1280x720', '24', '1'),
                                       ('fullhd_1080p', '1920x1080', '24', '0.5'), ('uhd_4k_one_frame', '3840x2160', '4', '0.25'),
                                       ('high_framerate_60fps', '320x180', '60', '1')]:
        w, h = map(int, size.split('x'))
        make(f'properties/{name}.mp4', [*source(size, rate, duration), *h264, '-an'],
             f'Dimension/frame-rate boundary: {size}, {rate} fps, {duration} seconds.', expected(video(width=w, height=h, r_frame_rate=f'{rate}/1')))
    make('properties/odd_dimensions.mkv', ['-f', 'lavfi', '-i', 'testsrc=size=321x181:rate=12:duration=1', '-c:v', 'ffv1', '-pix_fmt', 'yuv444p'],
         'Odd dimensions that cannot be represented by ordinary 4:2:0 encoders.', expected(video('ffv1', 321, 181, pix_fmt='yuv444p')))
    make('properties/rotated_90.mp4', ['-display_rotation', '90', '-i', basepath, '-map', '0', '-c', 'copy'],
         'Phone-style display rotation metadata; encoded dimensions stay 640x360.', expected(video(width=640, height=360, rotation=90), audio('aac')))
    make('properties/anamorphic_pal.mkv', [*source('720x576', '25', '1'), '-vf', 'setsar=16/15', *h264],
         'Non-square pixels: 720x576 encoded, 4:3 displayed.', expected(video(width=720, height=576, sample_aspect_ratio='16:15', display_aspect_ratio='4:3')))
    make('properties/interlaced_top_first.mpg', [*source('352x288', '50', '1'), '-vf', 'tinterlace=mode=interleave_top', '-c:v', 'mpeg2video', '-flags', '+ilme+ildct', '-top', '1', '-b:v', '800k', '-an', '-f', 'mpeg'],
         'Top-field-first interlaced MPEG-2, 25 frames / 50 fields per second.', expected(video('mpeg2video', 352, 288, field_order='tt')))
    make('properties/variable_frame_rate.mp4', [*source(duration='1.5'), '-vf', r'setpts=if(lt(N\,12)\,N/(12*TB)\,(1+(N-12)/24)/TB)', '-fps_mode', 'vfr', *h264, '-bf', '0'],
         'Two frame cadences in one file (12 then 24 fps); inspect packet timestamps.', expected(video(), variable_frame_rate=True))
    make('properties/audio_delayed_250ms.mp4', [*source(), '-itsoffset', '0.25', *tone(duration='1.75'), *h264, *aac],
         'Audio begins roughly 250ms after video; AAC priming introduces small variation.', expected(video(), audio('aac'), audio_video_start_delta_range=[0.20, 0.28]))
    make('properties/audio_longer_than_video.mp4', [*source(duration='1'), *tone(duration='2'), *h264, *aac],
         'Unequal stream lengths; do not assume audio and video end together.', expected(video(), audio('aac'), stream_duration_gap_min=0.8))
    make('properties/nonzero_start_timestamp.ts', ['-i', basepath, '-map', '0', '-c', 'copy', '-output_ts_offset', '5', '-f', 'mpegts'],
         'Transport-stream timestamps start away from zero.', expected(video(width=640, height=360), audio('aac'), start_time_min=5))
    make('properties/mp4_moov_at_end.mp4', ['-i', basepath, '-map', '0', '-c', 'copy'],
         'Ordinary MP4 with the moov index at the end; requires seekable input.', expected(video(width=640, height=360), audio('aac'), mp4_layout='moov_at_end'))
    make('properties/fragmented.mp4', ['-i', basepath, '-map', '0', '-c', 'copy', '-movflags', '+frag_keyframe+empty_moov+default_base_moof'],
         'Fragmented MP4 with moof boxes for progressive/pipe processing.', expected(video(width=640, height=360), audio('aac'), mp4_layout='fragmented'))
    make('properties/one_frame.mp4', [*source(), '-frames:v', '1', *h264, '-an'],
         'A valid single-frame video shorter than a typical seek interval.', expected(video(), duration=(0.03, 0.09)))

    # Audio formats, sample representations, channel layouts and signal edges.
    audio_cases = [
        ('pcm16_mono_8k.wav', 'pcm_s16le', 8000, 1, []),
        ('pcm16_stereo_44100.wav', 'pcm_s16le', 44100, 2, []),
        ('aiff_big_endian.aiff', 'pcm_s16be', 44100, 2, []),
        ('pcm24_stereo_96k.wav', 'pcm_s24le', 96000, 2, []),
        ('pcm32_stereo_48k.wav', 'pcm_s32le', 48000, 2, []),
        ('float32_stereo.wav', 'pcm_f32le', 48000, 2, []),
        ('pcm16_surround_71.wav', 'pcm_s16le', 48000, 8, ['-channel_layout', '7.1']),
        ('flac_24bit_96k.flac', 'flac', 96000, 2, ['-sample_fmt', 's32', '-bits_per_raw_sample', '24']),
        ('alac_lossless.m4a', 'alac', 44100, 2, []),
        ('mp3_cbr_128k.mp3', 'libmp3lame', 44100, 2, ['-b:a', '128k']),
        ('mp3_vbr.mp3', 'libmp3lame', 44100, 2, ['-q:a', '2']),
        ('aac_lc.m4a', 'aac', 48000, 2, ['-b:a', '96k']),
        ('aac_adts.aac', 'aac', 48000, 2, ['-b:a', '96k', '-f', 'adts']),
        ('opus.ogg', 'libopus', 48000, 2, ['-b:a', '64k']),
        ('vorbis.ogg', 'libvorbis', 44100, 2, ['-q:a', '3']),
        ('wma.wma', 'wmav2', 44100, 2, ['-b:a', '128k']),
        ('ac3_surround_51.ac3', 'ac3', 48000, 6, ['-channel_layout', '5.1', '-b:a', '384k']),
        ('eac3_surround_51.eac3', 'eac3', 48000, 6, ['-channel_layout', '5.1', '-b:a', '384k']),
        ('dts_surround_51.dts', 'dca', 48000, 6, ['-strict', '-2', '-channel_layout', '5.1(side)', '-b:a', '768k']),
        ('amr_nb_voice.amr', 'libopencore_amrnb', 8000, 1, ['-b:a', '12.2k']),
        ('mp2.mp2', 'mp2', 48000, 2, ['-b:a', '128k']),
        ('g711_mulaw.wav', 'pcm_mulaw', 8000, 1, []),
        ('g711_alaw.wav', 'pcm_alaw', 8000, 1, []),
    ]
    codec_names = {'libmp3lame': 'mp3', 'libopus': 'opus', 'libvorbis': 'vorbis', 'dca': 'dts', 'libopencore_amrnb': 'amr_nb'}
    for name, codec, rate, channels, options in audio_cases:
        make('audio/' + name, [*tone(rate, channels=channels), '-c:a', codec, *options],
             f'Audio-only conversion: {codec_names.get(codec, codec)}, {rate} Hz, {channels} channels; distinct tone per channel.',
             expected(audio(codec_names.get(codec, codec), rate, channels), duration=(1.9, 2.2)))
    make('audio/silence.wav', ['-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-t', '1', '-c:a', 'pcm_s16le'],
         'Digital silence for silence detection and zero-level handling.', expected(audio('pcm_s16le'), signal='silence'))
    make('audio/impulse_at_500ms.wav', ['-f', 'lavfi', '-i', r'aevalsrc=if(eq(n\,24000)\,0.8\,0):s=48000:d=1', '-c:a', 'pcm_s16le'],
         'One impulse at exactly sample 24000 for sample-accurate trim and resampling.', expected(audio('pcm_s16le', channels=1), signal='impulse', impulse_sample=24000))
    make('audio/quiet_minus40db.wav', ['-f', 'lavfi', '-i', 'aevalsrc=0.01*sin(2*PI*440*t):s=48000:d=1', '-c:a', 'pcm_s16le'],
         'Quiet signal with approximately -40 dBFS peak for normalization.', expected(audio('pcm_s16le', channels=1), peak_range=[0.009, 0.011]))
    make('audio/clipped.wav', ['-f', 'lavfi', '-i', 'aevalsrc=1.4*sin(2*PI*440*t):s=48000:d=1', '-c:a', 'pcm_s16le'],
         'Deliberate hard clipping at full scale for peak and distortion checks.', expected(audio('pcm_s16le', channels=1), peak_range=[0.999, 1.0]))
    make('audio/very_short_10ms.wav', [*tone(duration='0.01', channels=1), '-c:a', 'pcm_s16le'],
         'Audio shorter than one compressed-audio frame.', expected(audio('pcm_s16le', channels=1), duration=(0.009, 0.011)))
    make('audio/stereo_distinct_channels.wav', [*tone(), '-c:a', 'pcm_s16le'],
         'Left 440 Hz and right 660 Hz tones identify channel swaps and downmix behavior.', expected(audio('pcm_s16le'), stereo_tone_hz=[440, 660]))

    # Still/animated images and raw inputs. These are test patterns, not AI assets.
    for name, codec, pixel in [('color.png', 'png', 'rgb24'), ('color.jpg', 'mjpeg', 'yuvj420p'),
                               ('color.bmp', 'bmp', 'bgr24'), ('color.tiff', 'tiff', 'rgb24'), ('color.webp', 'libwebp', 'yuv420p')]:
        make('images/' + name, [*source(), '-frames:v', '1', '-c:v', codec, '-pix_fmt', pixel, '-update', '1'],
             'Single-image decode, thumbnail and conversion input.', expected(video({'libwebp': 'webp'}.get(codec, codec), pix_fmt=pixel)))
    make('images/alpha.png', ['-f', 'lavfi', '-i', 'color=c=blue@0.25:s=160x90:r=1,format=rgba', '-frames:v', '1', '-c:v', 'png', '-update', '1'],
         'RGBA PNG with transparency for overlay/compositing.', expected(video('png', 160, 90, pix_fmt='rgba'), alpha=True))
    make('images/grayscale16.png', [*source(), '-frames:v', '1', '-pix_fmt', 'gray16be', '-c:v', 'png', '-update', '1'],
         '16-bit grayscale image conversion.', expected(video('png', pix_fmt='gray16be')))
    make('images/animated.gif', [*source('160x90', '10', '1'), '-vf', 'split[a][b];[a]palettegen[p];[b][p]paletteuse', '-loop', '0'],
         'Animated GIF with palette, looping and frame timing.', expected(video('gif', 160, 90)))
    make('images/animated.apng', [*source('160x90', '10', '1'), '-c:v', 'apng', '-plays', '0', '-f', 'apng'],
         'Animated PNG with multiple frames.', expected(video('apng', 160, 90)))
    pattern = str(location('images/sequence/frame_%03d.png'))
    run([*source('160x90', '4', '1'), '-frames:v', '4', '-c:v', 'png', pattern], 'four-frame PNG sequence')
    for path in sorted((ROOT / 'images/sequence').glob('frame_*.png')):
        assets[path.relative_to(ROOT).as_posix()] = 'sequence-frame'
    sequence_args = ['-framerate', '4', '-start_number', '1']
    fixtures.append({'id': 'images.sequence', 'path': 'images/sequence/frame_%03d.png', 'scenario': 'Numbered PNG sequence at an explicit frame rate.', 'tags': ['images', 'sequence'], 'outcome': 'accept', 'input_args': sequence_args, 'expected': expected(video('png', 160, 90), duration=(0.95, 1.05)), 'pattern': True})
    make('raw/yuv420p_160x90_12fps.yuv', [*source('160x90', '12', '1'), '-c:v', 'rawvideo', '-pix_fmt', 'yuv420p', '-f', 'rawvideo'],
         'Headerless raw video; callers must provide pixel format, dimensions and frame rate.', expected(video('rawvideo', 160, 90, pix_fmt='yuv420p'), duration=(0.95, 1.05)),
         input_args=['-f', 'rawvideo', '-pixel_format', 'yuv420p', '-video_size', '160x90', '-framerate', '12'])
    make('raw/pcm_s16le_mono_16k.pcm', [*tone(16000, duration='1', channels=1), '-c:a', 'pcm_s16le', '-f', 's16le'],
         'Headerless signed 16-bit little-endian PCM.', expected(audio('pcm_s16le', 16000, 1), duration=(0.95, 1.05)), input_args=['-f', 's16le', '-sample_rate', '16000', '-ch_layout', 'mono'])
    make('raw/h264_annexb.h264', ['-i', basepath, '-map', '0:v:0', '-c:v', 'copy', '-bsf:v', 'h264_mp4toannexb', '-f', 'h264'],
         'Elementary H.264 Annex B bitstream without container timestamps.', expected(video(width=640, height=360)))
    make('raw/hevc_annexb.hevc', ['-i', str(ROOT / 'video/hevc_main_aac.mp4'), '-map', '0:v:0', '-c:v', 'copy', '-bsf:v', 'hevc_mp4toannexb', '-f', 'hevc'],
         'Elementary HEVC Annex B bitstream.', expected(video('hevc')))

    # Text sidecars with encoding, markup and overlapping timing edges.
    english = '1\n00:00:00,100 --> 00:00:01,200\nHello, FFmpeg!\nSecond line.\n\n2\n00:00:01,300 --> 00:00:02,800\n<i>Italic</i> & punctuation: "quotes".\n\n'
    multilingual = '1\n00:00:00,000 --> 00:00:01,300\n日本語・中文・한국어\n\n2\n00:00:01,200 --> 00:00:02,900\nCafé — Ελληνικά — العربية — 🎬\n\n'
    ass = ('[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n\n[V4+ Styles]\n'
           'Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n'
           'Style: Default,Arial,24,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,1,0,2,10,10,10,1\n\n[Events]\n'
           'Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n'
           'Dialogue: 0,0:00:00.10,0:00:01.30,Default,,0,0,0,,{\\b1}Bold{\\b0} and {\\i1}italic{\\i0}\\NSecond line\n'
           'Dialogue: 0,0:00:01.40,0:00:02.90,Default,,0,0,0,,{\\pos(320,180)}Positioned subtitle\n')
    subtitle_cases = [
        ('english.srt', english, 'subrip', []),
        ('unicode_overlap.srt', multilingual, 'subrip', []),
        ('utf8_bom_crlf.srt', b'\xef\xbb\xbf' + multilingual.replace('\n', '\r\n').encode('utf-8'), 'subrip', []),
        ('latin1.srt', '1\n00:00:00,100 --> 00:00:01,800\nCafé, déjà vu, Straße.\n\n'.encode('iso-8859-1'), 'subrip', ['-sub_charenc', 'ISO-8859-1']),
        ('styled.ass', ass, 'ass', []),
        ('captions.vtt', 'WEBVTT\n\n00:00.100 --> 00:01.200\nHello <i>WebVTT</i>.\n\n00:01.300 --> 00:02.900\n日本語 captions\n\n', 'webvtt', []),
    ]
    for name, content, codec, input_options in subtitle_cases:
        resource('subtitles/' + name, content)
        add('subtitles/' + name, 'Subtitle parsing, text extraction and character encoding.', expected({'codec_type': 'subtitle', 'codec_name': codec}, subtitle_cues_min=1), input_args=input_options)
    chapter_text = ';FFMETADATA1\ntitle=Synthetic chapter fixture\nartist=FFmpegSharpApi\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1500\ntitle=Introduction\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=1500\nEND=3000\ntitle=Second chapter\n'
    resource('metadata/chapters.ffmetadata', chapter_text)
    resource('attachments/readme.txt', 'Synthetic attachment used to test extraction.\nAll media in this pack is generated from test patterns and mathematical signals.\n')
    multi_args = ['-i', basepath, '-f', 'lavfi', '-i', 'sine=frequency=880:sample_rate=48000:duration=3',
                  '-i', str(ROOT / 'subtitles/english.srt'), '-i', str(ROOT / 'subtitles/styled.ass'), '-i', str(ROOT / 'metadata/chapters.ffmetadata'),
                  '-map', '0:v:0', '-map', '0:a:0', '-map', '1:a:0', '-map', '2:s:0', '-map', '3:s:0', '-map_metadata', '4', '-map_chapters', '4',
                  '-c:v', 'copy', '-c:a:0', 'copy', '-c:a:1', 'flac', '-c:s:0', 'srt', '-c:s:1', 'ass',
                  '-metadata:s:a:0', 'language=eng', '-metadata:s:a:0', 'title=English stereo', '-disposition:a:0', 'default',
                  '-metadata:s:a:1', 'language=jpn', '-metadata:s:a:1', 'title=Japanese alternate mono', '-disposition:a:1', '0',
                  '-metadata:s:s:0', 'language=eng', '-disposition:s:0', 'default', '-metadata:s:s:1', 'language=jpn', '-disposition:s:1', 'forced',
                  '-attach', str(ROOT / 'attachments/readme.txt'), '-metadata:s:t:0', 'mimetype=text/plain', '-metadata:s:t:0', 'filename=readme.txt']
    make('metadata/multitrack_chapters_attachment.mkv', multi_args,
         'Stream selection: two languages/audio codecs, default/forced subtitles, two chapters and a text attachment.',
         expected(video(width=640, height=360), audio('aac', tags={'language': 'eng'}, disposition={'default': 1}),
                  audio('flac', channels=1, tags={'language': 'jpn'}, disposition={'default': 0}),
                  {'codec_type': 'subtitle', 'codec_name': 'subrip', 'tags': {'language': 'eng'}, 'disposition': {'default': 1}},
                  {'codec_type': 'subtitle', 'codec_name': 'ass', 'tags': {'language': 'jpn'}, 'disposition': {'forced': 1}},
                  {'codec_type': 'attachment', 'tags': {'mimetype': 'text/plain', 'filename': 'readme.txt'}}, chapter_count=2, subtitle_cues_min=2, attachment_sha256=sha256(ROOT / 'attachments/readme.txt')))
    make('metadata/mp4_embedded_subtitles.mp4', ['-i', basepath, '-i', str(ROOT / 'subtitles/unicode_overlap.srt'), '-map', '0', '-map', '1:s', '-c', 'copy', '-c:s', 'mov_text', '-metadata:s:s:0', 'language=jpn'],
         'MP4 mov_text subtitles containing Unicode and overlapping source cues.', expected(video(width=640, height=360), audio('aac'), {'codec_type': 'subtitle', 'codec_name': 'mov_text'}, subtitle_cues_min=1))
    make('metadata/subtitle_only.mks', ['-i', str(ROOT / 'subtitles/english.srt'), '-c:s', 'srt', '-f', 'matroska'],
         'Subtitle-only Matroska: no audio or video streams.', expected({'codec_type': 'subtitle', 'codec_name': 'subrip'}, subtitle_cues_min=2))
    make('metadata/audio_unicode_tags.m4a', [*tone(), *aac, '-metadata', 'title=日本語 Café 🎵', '-metadata', 'artist=FFmpegSharpApi', '-metadata', 'comment=Quotes " & equals = semicolon ;'],
         'Unicode metadata and punctuation survive demux/remux.', expected(audio('aac'), format_tags={'title': '日本語 Café 🎵', 'artist': 'FFmpegSharpApi'}))
    make('metadata/mp3_cover_art.mp3', ['-i', str(ROOT / 'audio/mp3_cbr_128k.mp3'), '-i', str(ROOT / 'images/color.jpg'), '-map', '0:a', '-map', '1:v', '-c', 'copy', '-id3v2_version', '3', '-disposition:v', 'attached_pic', '-metadata:s:v', 'title=Album cover'],
         'MP3 with embedded JPEG cover art; distinguish cover art from motion video.', expected(audio('mp3', 44100), video('mjpeg', pix_fmt='yuvj420p', disposition={'attached_pic': 1})))

    # Local on-demand streaming, with every member tracked by hash.
    make('streaming/transport_stream.ts', ['-i', basepath, '-map', '0', '-c', 'copy', '-f', 'mpegts'],
         'Standalone MPEG-TS with H.264/AAC.', expected(video(width=640, height=360), audio('aac')))
    for folder, kind in [('hls_ts', 'mpegts'), ('hls_fmp4', 'fmp4')]:
        output = f'streaming/{folder}/index.m3u8'
        extension = 'ts' if kind == 'mpegts' else 'm4s'
        arguments = ['-i', basepath, '-map', '0', '-c', 'copy', '-f', 'hls', '-hls_time', '1', '-hls_list_size', '0', '-hls_playlist_type', 'vod', '-hls_segment_type', kind,
                     '-hls_segment_filename', str(location(f'streaming/{folder}/segment_%03d.{extension}'))]
        if kind == 'fmp4':
            arguments += ['-hls_fmp4_init_filename', 'init.mp4']
        make(output, arguments, f'Local HLS VOD playlist using {kind} segments; test playlist-relative paths.', expected(video(width=640, height=360), audio('aac'), duration=(2.95, 3.15)))
        for member in (ROOT / f'streaming/{folder}').iterdir():
            assets.setdefault(member.relative_to(ROOT).as_posix(), 'streaming-member')
    dash = 'streaming/dash/manifest.mpd'
    make(dash, ['-i', basepath, '-map', '0', '-c', 'copy', '-f', 'dash', '-seg_duration', '1', '-use_template', '1', '-use_timeline', '1'],
         'Local MPEG-DASH manifest with separate audio/video fragmented MP4 representations.', expected(video(width=640, height=360), audio('aac'), duration=(2.95, 3.15)))
    for member in (ROOT / 'streaming/dash').iterdir():
        assets.setdefault(member.relative_to(ROOT).as_posix(), 'streaming-member')

    # Portable local concat lists and filename/extension handling.
    concat = "ffconcat version 1.0\n# Include AAC encoder padding to keep DTS monotonic at the join.\nfile '../video/h264_aac_stereo_faststart.mp4'\nduration 3.024\nfile '../video/h264_aac_stereo_faststart.mp4'\nduration 3.024\n"
    resource('editing/two_clips.ffconcat', concat)
    add('editing/two_clips.ffconcat', 'Concat demuxer: two compatible clips, relative paths, about six seconds including AAC padding.', expected(video(width=640, height=360), audio('aac'), duration=(6.0, 6.15)), input_args=['-safe', '0'])
    for name in ['paths/file with spaces.mp4', 'paths/日本語 café 🎬/映像.mp4', 'paths/clip [01] & 100% #1.mp4',
                 'paths/no_extension', 'paths/mp4_content_wrong_extension.dat', 'paths/' + 'long_directory_' + 'x' * 70 + '/long_filename_' + 'y' * 45 + '.mp4']:
        location(name).write_bytes((ROOT / base).read_bytes())
        add(name, 'Open media using literal Unicode/space/punctuation/long paths or content-based format detection.', expected(video(width=640, height=360), audio('aac')), tags=['paths'])

    # Deliberately malformed inputs must be handled without hangs or crashes.
    invalids = {
        'invalid/empty.mp4': b'',
        'invalid/text_disguised_as_video.mp4': b'This is a synthetic text document, not a video.\n',
        'invalid/random_seed_2026.bin': random.Random(2026).randbytes(4096),
        'invalid/mp4_truncated_header.mp4': (ROOT / 'properties/mp4_moov_at_end.mp4').read_bytes()[:32],
        'invalid/malformed_riff.wav': b'RIFF\xff\xff\xff\x7fWAVEfmt \x01\x00\x00\x00\x00',
        'invalid/malformed.srt': b'1\ninvalid timestamp --> no end time\nbroken subtitle\n',
        'invalid/missing_segment.m3u8': b'#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1.0,\ndoes-not-exist.ts\n#EXT-X-ENDLIST\n',
        'invalid/missing_concat.ffconcat': b"ffconcat version 1.0\nfile 'does-not-exist.mp4'\n",
    }
    for name, content in invalids.items():
        resource(name, content)
        add(name, 'Expected rejection: return an error promptly, with no process crash.', {}, outcome='reject', input_args=['-safe', '0'] if name.endswith('.ffconcat') else [])
    ts = (ROOT / 'streaming/transport_stream.ts').read_bytes()
    resource('damaged/truncated_transport_stream.ts', ts[:(len(ts) // 376) * 188])
    add('damaged/truncated_transport_stream.ts', 'Half-length transport stream; best-effort recovery or clean error are both allowed.', {}, outcome='recover_or_reject')
    wav = bytearray((ROOT / 'audio/pcm16_stereo_44100.wav').read_bytes())
    resource('damaged/truncated_pcm.wav', bytes(wav[:len(wav) // 2]))
    add('damaged/truncated_pcm.wav', 'Declared WAV length exceeds available samples; best-effort recovery or clean error.', {}, outcome='recover_or_reject')
    corrupt = bytearray((ROOT / base).read_bytes())
    midpoint = len(corrupt) // 2
    corrupt[midpoint:midpoint + 256] = b'\xff' * 256
    resource('damaged/corrupt_video_packet.mp4', bytes(corrupt))
    add('damaged/corrupt_video_packet.mp4', 'Valid MP4 structure with injected packet damage; recovery behavior may vary by decoder.', {}, outcome='recover_or_reject')

    asset_entries = [{'path': name, 'role': role, 'bytes': (ROOT / name).stat().st_size, 'sha256': sha256(ROOT / name)} for name, role in sorted(assets.items())]
    manifest = {'schema_version': 1, 'generated_utc': datetime.now(timezone.utc).isoformat(),
                'origin': 'Synthetic lavfi test patterns, mathematical tones and authored text; no downloaded media.',
                'reference_encoder': {'executable': str(binary), 'version': version},
                'fixtures': fixtures, 'assets': asset_entries, 'generation_commands': commands}
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    catalog = ['# Fixture catalog', '', 'Generated by `generate_fixtures.py`. Exact stream expectations, input options, hashes and commands are in `manifest.json`.', '',
               '| Fixture | Expected behavior | Scenario |', '| --- | --- | --- |']
    for item in fixtures:
        target = item['path'].replace('%03d', '001')
        catalog.append(f'| [{item["path"]}]({quote(target, safe="/")}) | {item["outcome"]} | {item["scenario"]} |')
    (ROOT / 'CATALOG.md').write_text('\n'.join(catalog) + '\n', encoding='utf-8')
    size = sum(item['bytes'] for item in asset_entries)
    print(f'Generated {len(fixtures)} cases, {len(asset_entries)} files, {size / 1024 / 1024:.2f} MiB. Run validate_fixtures.py next.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (RuntimeError, subprocess.SubprocessError, OSError) as exc:
        print(f'Generation failed: {exc}', file=sys.stderr)
        sys.exit(1)
