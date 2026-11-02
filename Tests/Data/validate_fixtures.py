#!/usr/bin/env python3
"""Check fixture integrity and exercise a selected FFmpeg/FFprobe build."""
from __future__ import annotations

import argparse
from array import array
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parent


def invoke(argv: list[str], timeout: float = 30, *, data: bytes | None = None) -> subprocess.CompletedProcess:
    result = subprocess.run([str(v) for v in argv], input=data, capture_output=True, cwd=ROOT, timeout=timeout)
    code = result.returncode
    if (os.name != 'nt' and code < 0) or ((code & 0xF0000000) == 0xC0000000):
        raise AssertionError(f'Process crashed (exit {code:#x}): {argv[0]}')
    return result


def require(result: subprocess.CompletedProcess, what: str) -> bytes:
    if result.returncode:
        raise AssertionError(f'{what}: exit {result.returncode}: {result.stderr.decode("utf-8", "replace")[-3000:]}')
    return result.stdout


def same(actual, expected, where: str) -> None:
    if isinstance(expected, dict):
        assert isinstance(actual, dict), f'{where}: expected object, got {actual!r}'
        for key, value in expected.items():
            assert key in actual, f'{where}: missing {key}'
            same(actual[key], value, f'{where}.{key}')
    else:
        assert actual == expected, f'{where}: expected {expected!r}, got {actual!r}'


def top_level_boxes(path: Path) -> list[str]:
    boxes = []
    with path.open('rb') as handle:
        total = path.stat().st_size
        while handle.tell() + 8 <= total:
            start = handle.tell()
            size, kind = struct.unpack('>I4s', handle.read(8))
            header = 8
            if size == 1:
                size = struct.unpack('>Q', handle.read(8))[0]
                header = 16
            elif size == 0:
                size = total - start
            assert size >= header and start + size <= total, f'Invalid MP4 box at offset {start}'
            boxes.append(kind.decode('ascii', 'replace'))
            handle.seek(start + size)
    return boxes


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, 'reconfigure'):
            stream.reconfigure(encoding='utf-8', errors='replace')
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ffmpeg', default='ffmpeg', help='Binary under test')
    parser.add_argument('--ffprobe', default='ffprobe', help='Companion probe binary')
    parser.add_argument('--report', default='validation-report.json', help='JSON report path, relative to Tests unless absolute')
    parser.add_argument('--timeout', type=float, default=30, help='Timeout per process in seconds')
    parser.add_argument('--fixtures-only', action='store_true', help='Skip transformation smoke tests')
    args = parser.parse_args()
    assert args.timeout > 0, '--timeout must be positive'
    ffmpeg = shutil.which(args.ffmpeg) or args.ffmpeg
    ffprobe = shutil.which(args.ffprobe) or args.ffprobe
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
    assert manifest['schema_version'] == 1, 'Unsupported manifest schema'
    started = time.monotonic()
    report = {'schema_version': 1, 'validated_utc': datetime.now(timezone.utc).isoformat(),
              'ffmpeg': require(invoke([ffmpeg, '-version'], args.timeout), 'ffmpeg version').decode('utf-8', 'replace'),
              'ffprobe': require(invoke([ffprobe, '-version'], args.timeout), 'ffprobe version').decode('utf-8', 'replace'),
              'integrity': [], 'fixtures': [], 'operations': []}

    def checked_path(relative: str) -> Path:
        path = (ROOT / relative).resolve()
        assert path.is_relative_to(ROOT), f'Path escapes fixture root: {relative}'
        return path

    def execute(arguments: list[str], *, strict: bool = True, data: bytes | None = None) -> subprocess.CompletedProcess:
        global_args = [ffmpeg, '-hide_banner', '-nostdin', '-v', 'error', '-threads', '2', '-filter_threads', '1', '-filter_complex_threads', '1']
        if strict:
            global_args += ['-xerror', '-err_detect', 'careful']
        return invoke([*global_args, *arguments], args.timeout, data=data)

    def probe(path: Path, input_args: list[str] | None = None, extra: list[str] | None = None) -> dict:
        result = invoke([ffprobe, '-v', 'error', *(['-show_streams', '-show_format', '-show_chapters'] if extra is None else extra),
                         '-of', 'json', *(input_args or []), str(path)], args.timeout)
        return json.loads(require(result, f'probe {path.name}'))

    for asset in manifest['assets']:
        entry = {'path': asset['path']}
        try:
            path = checked_path(asset['path'])
            assert path.stat().st_size == asset['bytes'], 'File size changed'
            with path.open('rb') as handle:
                digest = hashlib.file_digest(handle, 'sha256').hexdigest()
            assert digest == asset['sha256'], 'SHA-256 mismatch'
            entry['status'] = 'pass'
        except (OSError, AssertionError) as exc:
            entry.update(status='fail', error=str(exc))
        report['integrity'].append(entry)

    for fixture in manifest['fixtures']:
        entry = {'id': fixture['id'], 'path': fixture['path'], 'outcome': fixture['outcome']}
        begin = time.monotonic()
        try:
            path = checked_path(fixture['path'])
            options = fixture['input_args']
            if fixture['outcome'] in ('reject', 'recover_or_reject'):
                # Probe and demux all packets. Recovery fixtures additionally decode A/V.
                result = invoke([ffprobe, '-v', 'error', '-show_format', '-show_streams', '-of', 'json', *options, str(path)], args.timeout)
                decoded = execute([*options, '-i', str(path), '-map', '0:v?', '-map', '0:a?', '-f', 'null', '-'], strict=False)
                entry.update(probe_exit=result.returncode, decode_exit=decoded.returncode,
                             diagnostic=decoded.stderr.decode('utf-8', 'replace')[-2000:])
                if fixture['outcome'] == 'reject':
                    assert result.returncode != 0, 'Expected ffprobe to reject malformed input'
                    assert decoded.returncode != 0, 'Expected FFmpeg to reject malformed input'
                else:
                    entry['recovery'] = 'decoded' if decoded.returncode == 0 else 'clean_error'
            else:
                info = probe(path, options)
                streams = info.get('streams', [])
                wanted = fixture['expected']
                assert len(streams) == len(wanted['streams']), f'Expected {len(wanted["streams"])} streams, got {len(streams)}'
                for index, (stream, expected_stream) in enumerate(zip(streams, wanted['streams'])):
                    properties = {k: v for k, v in expected_stream.items() if k not in ('min_b_frames', 'rotation')}
                    same(stream, properties, f'stream[{index}]')
                    if 'min_b_frames' in expected_stream:
                        assert stream.get('has_b_frames', 0) >= expected_stream['min_b_frames'], 'Expected reordered B frames'
                    if 'rotation' in expected_stream:
                        rotations = [item['rotation'] for item in stream.get('side_data_list', []) if 'rotation' in item]
                        assert any(float(value) % 360 == expected_stream['rotation'] % 360 for value in rotations), f'Unexpected rotation: {rotations}'
                fmt = info.get('format', {})
                if 'duration_range' in wanted:
                    duration = float(fmt.get('duration', 'nan'))
                    low, high = wanted['duration_range']
                    assert low <= duration <= high, f'Duration {duration} outside [{low}, {high}]'
                if 'format_tags' in wanted:
                    same(fmt.get('tags', {}), wanted['format_tags'], 'format.tags')
                if 'chapter_count' in wanted:
                    assert len(info.get('chapters', [])) == wanted['chapter_count'], 'Chapter count mismatch'
                    same([c.get('tags', {}).get('title') for c in info['chapters']], ['Introduction', 'Second chapter'], 'chapter titles')
                if 'mp4_layout' in wanted:
                    boxes = top_level_boxes(path)
                    if wanted['mp4_layout'] == 'faststart':
                        assert boxes.index('moov') < boxes.index('mdat'), f'Not faststart: {boxes}'
                    elif wanted['mp4_layout'] == 'moov_at_end':
                        assert boxes.index('moov') > boxes.index('mdat'), f'moov is not at end: {boxes}'
                    else:
                        assert 'moof' in boxes, 'No fragmented MP4 moof box'
                if 'start_time_min' in wanted:
                    assert float(fmt['start_time']) >= wanted['start_time_min'], 'Expected nonzero start timestamp'
                video_streams = [s for s in streams if s['codec_type'] == 'video']
                audio_streams = [s for s in streams if s['codec_type'] == 'audio']
                if 'audio_video_start_delta_range' in wanted:
                    delta = float(audio_streams[0]['start_time']) - float(video_streams[0]['start_time'])
                    low, high = wanted['audio_video_start_delta_range']
                    assert low <= delta <= high, f'A/V start delta {delta} outside [{low}, {high}]'
                if 'stream_duration_gap_min' in wanted:
                    gap = float(audio_streams[0]['duration']) - float(video_streams[0]['duration'])
                    assert gap >= wanted['stream_duration_gap_min'], f'Expected unequal stream lengths, got gap {gap}'
                if 'frame_properties' in wanted:
                    # Newer probes can expose HDR properties only on decoded frames.
                    frames = probe(path, options, ['-select_streams', 'v:0', '-read_intervals', '%+#1', '-show_frames'])['frames']
                    assert frames, 'No decoded frame for color/side-data validation'
                    same(frames[0], wanted['frame_properties'], 'first decoded frame')
                    side_types = {item['side_data_type'] for item in frames[0].get('side_data_list', [])}
                    assert set(wanted.get('frame_side_data_types', [])) <= side_types, f'Missing HDR side data: {side_types}'
                    entry['first_frame'] = frames[0]
                if wanted.get('variable_frame_rate'):
                    packets = probe(path, options, ['-select_streams', 'v:0', '-show_packets', '-show_entries', 'packet=pts_time'])['packets']
                    timestamps = sorted(float(item['pts_time']) for item in packets)
                    deltas = {round(b - a, 4) for a, b in zip(timestamps, timestamps[1:])}
                    assert len(deltas) >= 2 and max(deltas) > 1.5 * min(deltas), f'Expected variable cadence: {deltas}'
                    entry['frame_intervals_seconds'] = sorted(deltas)
                if video_streams or audio_streams:
                    require(execute([*options, '-i', str(path), '-map', '0:v?', '-map', '0:a?', '-f', 'null', '-']), 'full A/V decode')
                for index, _stream in enumerate(s for s in streams if s['codec_type'] == 'subtitle'):
                    text = require(execute([*options, '-i', str(path), '-map', f'0:s:{index}', '-c:s', 'srt', '-f', 'srt', 'pipe:1']), 'subtitle extraction').decode('utf-8')
                    assert text.count(' --> ') >= wanted.get('subtitle_cues_min', 1), 'Missing extracted subtitle cues'
                    if path.name in ('unicode_overlap.srt', 'utf8_bom_crlf.srt'):
                        assert '日本語' in text and 'Café' in text, 'Unicode subtitle text lost'
                    if path.name == 'latin1.srt':
                        assert 'Café' in text and 'Straße' in text, 'Latin-1 subtitle decoding failed'
                if 'attachment_sha256' in wanted:
                    attachment = require(execute(['-dump_attachment:t:0', 'pipe:1', '-i', str(path), '-map', '0:v:0', '-frames:v', '1', '-f', 'null', '-']), 'attachment extraction')
                    assert hashlib.sha256(attachment).hexdigest() == wanted['attachment_sha256'], 'Attachment contents changed'
                if wanted.get('alpha'):
                    rgba = require(execute(['-noautorotate', '-i', str(path), '-map', '0:v:0', '-frames:v', '1', '-pix_fmt', 'rgba', '-f', 'rawvideo', 'pipe:1']), 'alpha decode')
                    values = rgba[3::4]
                    assert values and all(0 < value < 255 for value in values), 'Expected semi-transparent alpha samples'
                if any(key in wanted for key in ('signal', 'peak_range', 'stereo_tone_hz')):
                    raw = require(execute(['-i', str(path), '-map', '0:a:0', '-c:a', 'pcm_f32le', '-f', 'f32le', 'pipe:1']), 'audio signal extraction')
                    samples = array('f')
                    samples.frombytes(raw)
                    if sys.byteorder != 'little':
                        samples.byteswap()
                    peak = max(abs(value) for value in samples)
                    if wanted.get('signal') == 'silence':
                        assert peak == 0, f'Expected digital silence, peak={peak}'
                    if wanted.get('signal') == 'impulse':
                        nonzero = [i for i, value in enumerate(samples) if abs(value) > 0.01]
                        assert nonzero == [wanted['impulse_sample']], f'Impulse at unexpected samples: {nonzero[:10]}'
                    if 'peak_range' in wanted:
                        low, high = wanted['peak_range']
                        assert low <= peak <= high, f'Peak {peak} outside [{low}, {high}]'
                    if 'stereo_tone_hz' in wanted:
                        for channel, hz in enumerate(wanted['stereo_tone_hz']):
                            channel_samples = samples[channel::2]
                            crossings = sum(a <= 0 < b for a, b in zip(channel_samples, channel_samples[1:]))
                            observed_hz = crossings * int(audio_streams[0]['sample_rate']) / len(channel_samples)
                            assert abs(observed_hz - hz) < 2, f'Channel {channel}: expected {hz} Hz, got {observed_hz}'
                entry['probe'] = info
            entry['status'] = 'pass'
        except (AssertionError, KeyError, ValueError, OSError, subprocess.SubprocessError) as exc:
            entry.update(status='fail', error=str(exc))
        entry['elapsed_seconds'] = round(time.monotonic() - begin, 3)
        report['fixtures'].append(entry)
        print(f'{entry["status"].upper():4} {entry["path"]}' + (f': {entry["error"]}' if entry['status'] == 'fail' else ''), flush=True)

    if not args.fixtures_only:
        run_operations(execute, probe, report['operations'])
    report['elapsed_seconds'] = round(time.monotonic() - started, 3)
    report['summary'] = {section: dict(Counter(item['status'] for item in report[section])) for section in ('integrity', 'fixtures', 'operations')}
    output = Path(args.report)
    if not output.is_absolute():
        output = ROOT / output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report['summary']), flush=True)
    print(f'Report: {output}')
    return 1 if any(item['status'] == 'fail' for section in ('integrity', 'fixtures', 'operations') for item in report[section]) else 0


def run_operations(execute, probe, results: list[dict]) -> None:
    """End-to-end user tasks; all outputs are temporary and removed on exit."""
    base = ROOT / 'video/h264_aac_stereo_faststart.mp4'

    def operation(name, action):
        begin = time.monotonic()
        record = {'name': name}
        try:
            details = action()
            record.update(status='pass', **(details or {}))
        except (AssertionError, KeyError, ValueError, OSError, subprocess.SubprocessError) as exc:
            record.update(status='fail', error=str(exc))
        record['elapsed_seconds'] = round(time.monotonic() - begin, 3)
        results.append(record)
        print(f'{record["status"].upper():4} operation: {name}' + (f': {record["error"]}' if record['status'] == 'fail' else ''), flush=True)

    with tempfile.TemporaryDirectory(prefix='ffmpegsharp-smoke-') as temporary:
        work = Path(temporary)

        def output_probe(arguments: list[str], output: Path) -> dict:
            require(execute(['-y', *arguments, str(output)]), output.name)
            info = probe(output)
            require(execute(['-i', str(output), '-map', '0:v?', '-map', '0:a?', '-f', 'null', '-']), 'decode transformation output')
            return info

        def remux():
            info = output_probe(['-i', str(base), '-map', '0', '-c', 'copy'], work / 'remux.mkv')
            same([s['codec_name'] for s in info['streams']], ['h264', 'aac'], 'remux codecs')

        def scale_transcode():
            info = output_probe(['-i', str(base), '-vf', 'scale=160:90', '-c:v', 'mpeg4', '-q:v', '5', '-c:a', 'aac'], work / 'scaled.mp4')
            same(info['streams'][0], {'codec_name': 'mpeg4', 'width': 160, 'height': 90}, 'scaled output')

        def trim(seek_before: bool):
            seek = ['-ss', '1', '-i', str(base)] if seek_before else ['-i', str(base), '-ss', '1']
            info = output_probe([*seek, '-t', '0.75', '-c:v', 'mpeg4', '-q:v', '5', '-c:a', 'aac'], work / f'trim-{seek_before}.mp4')
            assert 0.70 <= float(info['format']['duration']) <= 0.85, 'Trim duration out of range'

        def extract_audio():
            info = output_probe(['-i', str(base), '-vn', '-ac', '1', '-ar', '16000', '-c:a', 'pcm_s16le'], work / 'audio.wav')
            same(info['streams'][0], {'codec_name': 'pcm_s16le', 'channels': 1, 'sample_rate': '16000'}, 'extracted audio')

        def thumbnail():
            info = output_probe(['-ss', '1.25', '-i', str(base), '-frames:v', '1', '-vf', 'scale=160:-2', '-c:v', 'png', '-update', '1'], work / 'thumbnail.png')
            same(info['streams'][0], {'codec_name': 'png', 'width': 160, 'height': 90}, 'thumbnail')

        def concat():
            info = output_probe(['-safe', '0', '-i', str(ROOT / 'editing/two_clips.ffconcat'), '-map', '0', '-c', 'copy'], work / 'concat.mkv')
            assert 5.9 <= float(info['format']['duration']) <= 6.3, 'Concat duration out of range'

        def stream_selection():
            info = output_probe(['-i', str(ROOT / 'metadata/multitrack_chapters_attachment.mkv'), '-map', '0:v:0', '-map', '0:a:1', '-map_chapters', '-1', '-c', 'copy'], work / 'selected.mkv')
            same([s['codec_name'] for s in info['streams']], ['h264', 'flac'], 'selected codecs')
            assert info['streams'][1]['tags']['language'] == 'jpn', 'Selected wrong language'

        def audio_filter():
            info = output_probe(['-i', str(ROOT / 'audio/pcm16_surround_71.wav'), '-af', 'volume=0.5,aresample=44100', '-ac', '2', '-c:a', 'pcm_s16le'], work / 'downmix.wav')
            same(info['streams'][0], {'channels': 2, 'sample_rate': '44100'}, 'downmix output')

        def video_filter():
            info = output_probe(['-i', str(base), '-vf', 'crop=320:180:0:0,transpose=1,fps=12', '-an', '-c:v', 'mpeg4', '-q:v', '5'], work / 'filtered.mp4')
            same(info['streams'][0], {'width': 180, 'height': 320, 'r_frame_rate': '12/1'}, 'filtered output')

        def pipe_decode():
            require(execute(['-i', 'pipe:0', '-map', '0:v', '-map', '0:a', '-f', 'null', '-'], data=base.read_bytes()), 'stdin faststart MP4 decode')

        def lossless_roundtrip():
            source = ROOT / 'audio/pcm16_stereo_44100.wav'
            output_probe(['-i', str(source), '-c:a', 'flac'], work / 'roundtrip.flac')
            decoded = require(execute(['-i', str(work / 'roundtrip.flac'), '-c:a', 'pcm_s16le', '-f', 's16le', 'pipe:1']), 'roundtrip PCM')
            original = require(execute(['-i', str(source), '-c:a', 'pcm_s16le', '-f', 's16le', 'pipe:1']), 'source PCM')
            assert decoded == original, 'Lossless roundtrip changed samples'

        def image_sequence():
            info = output_probe(['-framerate', '4', '-i', str(ROOT / 'images/sequence/frame_%03d.png'), '-c:v', 'mpeg4', '-q:v', '5'], work / 'sequence.mp4')
            assert abs(float(info['format']['duration']) - 1) < 0.05, 'Sequence duration mismatch'

        def subtitle_conversion():
            output = work / 'converted.vtt'
            require(execute(['-y', '-i', str(ROOT / 'subtitles/unicode_overlap.srt'), '-c:s', 'webvtt', str(output)]), 'SRT to WebVTT')
            text = output.read_text(encoding='utf-8')
            assert text.startswith('WEBVTT') and '日本語' in text and 'Café' in text, 'Subtitle conversion lost content'

        def seek_near_end():
            path = str(ROOT / 'properties/long_gop_15s.mp4')
            output = ['-frames:v', '1', '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1']
            fast = require(execute(['-ss', '13.25', '-i', path, *output]), 'long-GOP input seek')
            slow = require(execute(['-i', path, '-ss', '13.25', *output]), 'long-GOP output seek')
            assert len(fast) == 320 * 180 * 3 and fast == slow, 'Accurate input/output seeks returned different frames'

        def subtitle_burn_in():
            output = ['-frames:v', '1', '-pix_fmt', 'rgb24', '-f', 'rawvideo', 'pipe:1']
            original = require(execute(['-i', str(base), '-ss', '0.5', *output]), 'original subtitle frame')
            # Relative filter paths avoid Windows drive-colon escaping; cwd is Tests.
            burned = require(execute(['-i', str(base), '-ss', '0.5', '-vf', 'subtitles=subtitles/english.srt', *output]), 'burn subtitles (requires libass)')
            assert len(original) == len(burned) and original != burned, 'Subtitle filter did not change pixels'

        def alpha_overlay():
            info = output_probe(['-i', str(base), '-i', str(ROOT / 'images/alpha.png'), '-filter_complex', '[0:v][1:v]overlay=20:20[v]',
                                 '-map', '[v]', '-map', '0:a:0', '-t', '1', '-c:v', 'mpeg4', '-q:v', '5', '-c:a', 'aac'], work / 'overlay.mp4')
            same(info['streams'][0], {'width': 640, 'height': 360}, 'overlay output')

        for name, action in [('remux_mp4_to_mkv', remux), ('scale_and_transcode', scale_transcode),
                             ('input_seek_and_trim', lambda: trim(True)), ('output_seek_and_trim', lambda: trim(False)),
                             ('extract_resample_mono_audio', extract_audio), ('seek_thumbnail', thumbnail),
                             ('concat_stream_copy', concat), ('select_alternate_audio', stream_selection),
                             ('downmix_resample_volume', audio_filter), ('crop_rotate_change_fps', video_filter),
                             ('stdin_pipe_decode', pipe_decode), ('lossless_audio_roundtrip', lossless_roundtrip),
                             ('image_sequence_to_video', image_sequence), ('unicode_subtitle_conversion', subtitle_conversion),
                             ('long_gop_accurate_seek_near_end', seek_near_end), ('subtitle_burn_in_libass', subtitle_burn_in), ('transparent_png_overlay', alpha_overlay)]:
            operation(name, action)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (AssertionError, OSError, ValueError, subprocess.SubprocessError) as exc:
        print(f'Validation could not start: {exc}', file=sys.stderr)
        sys.exit(2)
