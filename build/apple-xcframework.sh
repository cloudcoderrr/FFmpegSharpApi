#!/usr/bin/env bash
# Builds an FFmpeg.xcframework (static) from the per-slice iOS / iOS-simulator /
# Mac Catalyst static libraries produced by the native-apple build. Runs on macOS.
#
# Inputs (environment / args):
#   $1           license flavour (lgpl|gpl), default lgpl
#   NATIVE_DIR   root of native artifacts (default artifacts/native); expects
#                <license>/<rid>/lib/*.a and <license>/ios-arm64/include
#   OUT_DIR      output directory for the xcframework (default artifacts/apple)
set -euo pipefail

LICENSE="${1:-lgpl}"
NATIVE_DIR="${NATIVE_DIR:-artifacts/native}/$LICENSE"
OUT_DIR="${OUT_DIR:-artifacts/apple}/$LICENSE"
WORK="${WORK_DIR:-build-work/xcframework}/$LICENSE"
mkdir -p "$WORK" "$OUT_DIR"

FFLIBS="libavcodec libavdevice libavfilter libavformat libavutil libswresample libswscale"

log() { printf '\033[1;36m[xcframework]\033[0m %s\n' "$*" >&2; }

# combine_slice <rid> <output.a> : merge all static libs (FFmpeg + its static
# dependencies such as dav1d and libxml2) for one rid into a self-contained archive.
combine_slice() {
  local rid="$1" out="$2" l
  local libs=()
  shopt -s nullglob
  for l in "$NATIVE_DIR/$rid/lib/"*.a; do
    libs+=("$l")
  done
  shopt -u nullglob
  [ ${#libs[@]} -gt 0 ] || { log "no static libs for $rid"; return 1; }
  log "merging $rid (${libs[*]##*/}) -> $(basename "$out")"
  libtool -static -o "$out" "${libs[@]}"
}

args=()

# Device slice.
if combine_slice ios-arm64 "$WORK/ios-arm64.a"; then
  args+=(-library "$WORK/ios-arm64.a" -headers "$NATIVE_DIR/ios-arm64/include")
fi

# Simulator slice (fat arm64 + x64).
sim=()
combine_slice iossimulator-arm64 "$WORK/iossim-arm64.a" && sim+=("$WORK/iossim-arm64.a")
combine_slice iossimulator-x64 "$WORK/iossim-x64.a" && sim+=("$WORK/iossim-x64.a")
if [ ${#sim[@]} -gt 0 ]; then
  lipo -create "${sim[@]}" -output "$WORK/iossim.a"
  args+=(-library "$WORK/iossim.a" -headers "$NATIVE_DIR/iossimulator-arm64/include")
fi

# Mac Catalyst slice (fat arm64 + x64).
cat=()
combine_slice maccatalyst-arm64 "$WORK/catalyst-arm64.a" && cat+=("$WORK/catalyst-arm64.a")
combine_slice maccatalyst-x64 "$WORK/catalyst-x64.a" && cat+=("$WORK/catalyst-x64.a")
if [ ${#cat[@]} -gt 0 ]; then
  lipo -create "${cat[@]}" -output "$WORK/catalyst.a"
  args+=(-library "$WORK/catalyst.a" -headers "$NATIVE_DIR/maccatalyst-arm64/include")
fi

[ ${#args[@]} -gt 0 ] || { log "no Apple slices found under $NATIVE_DIR"; exit 1; }

rm -rf "$OUT_DIR/FFmpeg.xcframework"
log "creating FFmpeg.xcframework"
xcodebuild -create-xcframework "${args[@]}" -output "$OUT_DIR/FFmpeg.xcframework"
log "done -> $OUT_DIR/FFmpeg.xcframework"
