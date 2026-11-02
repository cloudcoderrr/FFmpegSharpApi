# FFmpeg fixture pack

114 synthetic test cases in 135 media/sidecar files, about 15.6 MiB. The pack is intended for FFmpeg CLI builds and FFmpegSharpApi integration tests. It contains generated test patterns, mathematical audio signals and authored text. No downloaded movies, music, fonts or third-party sample media are included.

Start with [CATALOG.md](CATALOG.md) for every scenario. [manifest.json](manifest.json) is the machine-readable contract: expected stream properties, duration tolerances, required input options, success/error expectations, SHA-256 hashes, encoder version and the exact generation argument arrays.

| Folder | Coverage |
| --- | --- |
| `video/` | H.264 baseline/high/10-bit/RGB, HEVC Main/Main10, AV1, VP8/VP9/10-bit, MPEG-4 Part 2, MPEG-2, H.263, WMV, FLV, Theora, MJPEG, ProRes 422/4444 alpha and FFV1; MP4, MKV, WebM, AVI, MOV, MPEG-PS, ASF, FLV, Ogg and 3GP |
| `properties/` | Portrait, 720p/1080p/4K, 60 fps, odd dimensions, rotation, non-square pixels, interlacing, variable frame rate, delayed/unequal audio, nonzero timestamps, faststart/end-moov/fragmented MP4, one-frame clips and long-GOP seeking |
| `audio/` | Integer/float PCM, WAV/AIFF, FLAC, ALAC, MP3 CBR/VBR, AAC M4A/ADTS, Opus, Vorbis, WMA, AC-3/E-AC-3/DTS, AMR-NB, MP2 and G.711; 8–96 kHz; mono/stereo/5.1/7.1; silence, impulse, quiet, clipped and very short signals |
| `images/` | PNG/JPEG/BMP/TIFF/WebP, transparency, 16-bit grayscale, animated GIF/APNG and numbered PNG sequence |
| `raw/` | Headerless YUV420P and PCM with explicit input parameters; H.264/HEVC Annex B elementary streams |
| `subtitles/` | SRT, ASS and WebVTT; styling, multiline/overlapping cues, Unicode, UTF-8 BOM/CRLF and ISO-8859-1 |
| `metadata/`, `attachments/` | Multiple audio/subtitle tracks, language/default/forced flags, chapters, attachment extraction, MP4 mov_text, subtitle-only Matroska, Unicode tags and MP3 cover art |
| `streaming/`, `editing/` | MPEG-TS, HLS TS/fMP4 VOD playlists, MPEG-DASH and concat-demuxer inputs; local segment/init files included |
| `paths/` | Spaces, Japanese/accented/emoji paths, punctuation, long paths, missing and incorrect filename extensions |
| `invalid/`, `damaged/` | Empty, disguised, random and malformed data; missing playlist/concat members; truncated streams and injected packet damage |

Run against a binary build (Python 3.11+; no Python packages required):

```powershell
python C:\Users\test\Desktop\Tools\FFmpegSharpApi\Tests\validate_fixtures.py `
  --ffmpeg C:\path\to\build\ffmpeg.exe `
  --ffprobe C:\path\to\build\ffprobe.exe `
  --report my-build-validation.json
```

Without binary arguments it uses `ffmpeg` and `ffprobe` on PATH. Relative report paths are written inside `Tests`. Exit status is 0 when every check passes, 1 for failures, and 2 if validation cannot start. `--fixtures-only` skips the operation tests; `--timeout 60` changes the default 30-second timeout per child process.

Validation checks all asset hashes, probes declared properties, decodes every accepted audio/video stream with `-xerror -err_detect careful`, extracts every subtitle stream, checks attachment contents and verifies selected signal/alpha/timestamp properties. HDR primaries, transfer and mastering metadata are checked on decoded frames because probe versions expose them differently. The 17 operation checks cover remuxing, scaling/transcoding, input/output seeking and trimming, audio extraction/resampling/downmix/volume, thumbnails, concat, alternate-stream selection, crop/rotation/fps, stdin piping, lossless audio roundtrip, image sequences, subtitle conversion/burn-in and transparent overlays. Outputs use an automatically removed temporary directory. Passing does not imply identical lossy pixels across different encoder versions.

Expected outcomes:

- `accept`: the declared properties and all applicable decode/content checks must pass.
- `reject`: both probe and FFmpeg must return a nonzero error promptly. Negative inputs are deliberate fixtures.
- `recover_or_reject`: partial recovery or a clean error is acceptable; the report records which occurred. A timeout or detected process crash fails validation. These are robustness samples, not a security fuzzer.

The checked-in [reference-validation.json](reference-validation.json) records the full reference run. [installed-validation.json](installed-validation.json) records the installed FFmpeg/FFprobe 8.0.3 run: 112/114 fixture cases and 16/17 operations pass. The installed build cannot decode AV1 in software on this machine, lacks the DASH demuxer, and lacks the libass subtitle-rendering filter. Those are build capability gaps, so the validator deliberately reports failures instead of silently skipping them. Add a software AV1 decoder such as libdav1d/libaom, DASH support (libxml2 for this FFmpeg version), and libass when those features belong in the target build.

Regenerate fixtures with a capable reference encoder:

```powershell
python C:\Users\test\Desktop\Tools\FFmpegSharpApi\Tests\generate_fixtures.py `
  --ffmpeg C:\path\to\reference\ffmpeg.exe
```

Generation overwrites named pack files and updates the manifest and catalog. It preserves unrelated files. It requires the usual built-in encoders plus libx264/libx264rgb, libx265, libvpx, libaom-av1, libmp3lame, libopus, libvorbis, libtheora, libwebp and libopencore_amrnb. The installed minimal encoder cannot regenerate the entire pack. Re-run validation after regeneration; previously saved reports then refer to the previous byte set. Media bytes can vary between encoder versions/runs even though the scenario expectations remain the same. `.gitattributes` prevents checkout line-ending conversion from altering fixture bytes, especially encoded subtitles and malformed inputs.

Generation used the FFmpeg 7.1 binary shipped in [imageio-ffmpeg 0.6.0](https://pypi.org/project/imageio-ffmpeg/0.6.0/). Independent verification used the FFmpeg/FFprobe 9.0.2 essentials build from [Gyan's build service](https://www.gyan.dev/ffmpeg/builds/); its downloaded ZIP SHA-256 was `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`, matching the published checksum. These supplemental tools were extracted under `%TEMP%\ffmpegsharp-fixture-tools`; they are not part of the test pack and did not replace installed binaries.

The HDR sample uses synthetic SDR pattern values with HDR tags and static metadata; it tests metadata handling, not colorimetric accuracy. Streaming tests use local VOD files. Live servers, networking/reconnection, DRM/encryption, hardware acceleration, bitmap subtitles, multi-hour/large-file stress, process cancellation and device capture require separate environment-dependent tests.
