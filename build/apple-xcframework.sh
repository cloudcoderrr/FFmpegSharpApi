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
# FFmpeg compiles some shared internal objects (e.g. framepool.o) into several
# libraries; a member that appears in more than one archive is kept only in the first
# so ForceLoad does not see duplicate symbols. Objects that merely share a basename
# within a single archive (dav1d's per-bit-depth templates) are left untouched.
combine_slice() {
  local rid="$1" out="$2"
  local libdir="$NATIVE_DIR/$rid/lib"
  shopt -s nullglob
  local archives=("$libdir"/*.a)
  shopt -u nullglob
  [ ${#archives[@]} -gt 0 ] || { log "no static libs for $rid"; return 1; }

  local tmp; tmp="$(mktemp -d)"
  local work=() a b base i=0
  for a in "${archives[@]}"; do
    b="$tmp/$(printf '%02d' "$i")_$(basename "$a")"
    cp "$a" "$b"; work+=("$b"); i=$((i + 1))
  done

  local seen="$tmp/seen"; : > "$seen"
  i=0
  for a in "${work[@]}"; do
    local d="$tmp/x$i"; mkdir -p "$d"; ( cd "$d" && ar x "$a" )
    shopt -s nullglob
    local o name syms sig
    for o in "$d"/*.o; do
      name="$(basename "$o")"
      # Signature = the object's defined global symbols. FFmpeg compiles some shared
      # internals (e.g. framepool.o) into several libs with identical symbols but
      # differing content, so dedup by symbols, not bytes. Objects with distinct
      # symbols (dav1d vs swscale ops.o) and symbol-less objects are always kept.
      syms="$(nm -gP "$o" 2>/dev/null | awk '$2 != "U" && $2 != "" {print $1}' | sort -u)"
      [ -n "$syms" ] || continue
      sig="$(printf '%s' "$syms" | shasum | cut -d' ' -f1)"
      if grep -qxF "$sig" "$seen"; then
        ar d "$a" "$name" 2>/dev/null || true
      else
        echo "$sig" >> "$seen"
      fi
    done
    shopt -u nullglob
    i=$((i + 1))
  done

  log "merging $rid (${#work[@]} archives) -> $(basename "$out")"
  libtool -static -o "$out" "${work[@]}"
  rm -rf "$tmp"
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
