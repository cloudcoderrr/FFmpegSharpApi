#!/usr/bin/env bash
# Builds FFmpeg (and any enabled external libraries) for a single RID + license
# flavour and stages the result under the output directory.
#
# Usage:
#   build.sh --rid <rid> --license <lgpl|gpl> [--deps "dav1d libxml2 ..."]
#            [--work <dir>] [--out <dir>]
#
# Environment (per family):
#   LLVM_MINGW_ROOT   required for win-* RIDs
#   ANDROID_NDK_ROOT  required for android-* RIDs
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
# shellcheck source=lib/common.sh
source "$HERE/lib/common.sh"
# shellcheck source=versions.env
source "$HERE/versions.env"

RID=""; LICENSE="lgpl"; DEPS=""
WORK="${GITHUB_WORKSPACE:-$REPO_ROOT}/build-work"
OUTDIR="$REPO_ROOT/artifacts/native"

while [ $# -gt 0 ]; do
  case "$1" in
    --rid)     RID="$2"; shift 2 ;;
    --license) LICENSE="$2"; shift 2 ;;
    --deps)    DEPS="$2"; shift 2 ;;
    --work)    WORK="$2"; shift 2 ;;
    --out)     OUTDIR="$2"; shift 2 ;;
    *) die "Unknown argument: $1" ;;
  esac
done

[ -n "$RID" ] || die "--rid is required"
case "$LICENSE" in lgpl|gpl) ;; *) die "--license must be lgpl or gpl" ;; esac

# GPL-only encoders are implied by the GPL flavour unless the caller overrode --deps.
if [ "$LICENSE" = "gpl" ] && [ -z "$DEPS" ]; then
  DEPS="dav1d libxml2 x264 x265"
fi

export RID
# shellcheck source=lib/toolchain.sh
source "$HERE/lib/toolchain.sh"
# shellcheck source=lib/deps.sh
source "$HERE/lib/deps.sh"

WORK="$WORK/$LICENSE/$RID"
PREFIX="$WORK/prefix"
mkdir -p "$PREFIX"

log "=== FFmpeg $FFMPEG_REF | rid=$RID | license=$LICENSE | deps='${DEPS:-none}' ==="
configure_toolchain
log "family=$FAMILY arch=$ARCH linkage=$LINKAGE host=$HOST_TRIPLE"

export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"

# --- external libraries -----------------------------------------------------
for d in $DEPS; do
  log "--- building dependency: $d ---"
  build_dep "$d"
done

# --- FFmpeg -----------------------------------------------------------------
clone_component ffmpeg "$FFMPEG_REPO" "$FFMPEG_REF" "$WORK/ffmpeg"
apply_patches ffmpeg "$WORK/ffmpeg" "$REPO_ROOT/patches"

cfg=(--prefix="$PREFIX"
  --disable-doc --disable-debug --disable-ffplay --disable-sdl2
  --enable-version3
  --pkg-config=pkg-config --pkg-config-flags=--static
  --target-os="$FF_TARGET_OS" --arch="$FF_ARCH")
[ "$FF_CROSS_COMPILE" = 1 ] && cfg+=(--enable-cross-compile)
[ -n "$FF_CROSS_PREFIX" ] && cfg+=(--cross-prefix="$FF_CROSS_PREFIX")
cfg+=("${FF_EXTRA_CONFIGURE[@]}")
[ "$LICENSE" = "gpl" ] && cfg+=(--enable-gpl)

# FFmpeg builds the programs by default; the mobile/static flavours drop them.
case "$FAMILY" in
  windows|linux|macos) cfg+=(--enable-ffmpeg --enable-ffprobe) ;;
  *)                   cfg+=(--disable-programs) ;;
esac

# shellcheck disable=SC2046
cfg+=($(ffmpeg_dep_flags))

( cd "$WORK/ffmpeg"
  log "configure ${cfg[*]}"
  ./configure "${cfg[@]}"
  make -j"$(ncpu)"
  make install )

# --- stage ------------------------------------------------------------------
OUT_RID="$OUTDIR/$LICENSE/$RID"
rm -rf "$OUT_RID"; mkdir -p "$OUT_RID/lib"

# Android forbids versioned sonames in an APK, so collapse libavcodec.so.63 -> libavcodec.so
# and rewrite the soname and NEEDED entries to match.
_stage_android_so() {
  local real base stem f need newneed
  for real in "$PREFIX"/lib/libav*.so.* "$PREFIX"/lib/libsw*.so.*; do
    [ -e "$real" ] || continue
    [ -L "$real" ] && continue
    base="$(basename "$real")"; stem="${base%%.so.*}"
    cp -v "$real" "$OUT_RID/lib/$stem.so"
  done
  for f in "$OUT_RID"/lib/*.so; do
    patchelf --set-soname "$(basename "$f")" "$f"
    while read -r need; do
      case "$need" in
        libav*.so.*|libsw*.so.*) newneed="${need%%.so.*}.so"; patchelf --replace-needed "$need" "$newneed" "$f" ;;
      esac
    done < <(patchelf --print-needed "$f")
    strip_binary "$STRIP" "$f"
  done
}

stage_shared() {
  case "$FAMILY" in
    windows)
      cp -v "$PREFIX"/bin/*.dll "$OUT_RID/lib/"
      for f in "$OUT_RID"/lib/*.dll; do strip_binary "$STRIP" "$f"; done ;;
    linux)
      cp -Pv "$PREFIX"/lib/*.so* "$OUT_RID/lib/"
      for f in "$OUT_RID"/lib/*.so*; do [ -L "$f" ] || strip_binary "$STRIP" "$f"; done ;;
    android)
      _stage_android_so ;;
    macos)
      cp -v "$PREFIX"/lib/*.dylib "$OUT_RID/lib/"
      for f in "$OUT_RID"/lib/*.dylib; do
        [ -L "$f" ] && continue
        install_name_tool -id "@rpath/$(basename "$f")" "$f" || true
        while read -r dep; do
          [ -n "$dep" ] || continue
          install_name_tool -change "$dep" "@rpath/$(basename "$dep")" "$f" || true
        done < <(otool -L "$f" | awk -v p="$PREFIX" '$1 ~ p {print $1}')
        strip_binary "$STRIP" "$f"
      done ;;
  esac
}

stage_static() {
  cp -v "$PREFIX"/lib/*.a "$OUT_RID/lib/"
}

if [ "$LINKAGE" = "shared" ]; then stage_shared; else stage_static; fi

# programs for desktop flavours
case "$FAMILY" in
  windows|linux|macos)
    mkdir -p "$OUT_RID/tools"
    for t in ffmpeg ffprobe; do
      for ext in "" ".exe"; do
        [ -f "$PREFIX/bin/$t$ext" ] && cp -v "$PREFIX/bin/$t$ext" "$OUT_RID/tools/" || true
      done
    done ;;
esac

# headers (identical across RIDs for a given license; packaged once downstream)
cp -r "$PREFIX/include" "$OUT_RID/include"

cat > "$OUT_RID/BUILDINFO.txt" <<EOF
ffmpeg_ref=$FFMPEG_REF
rid=$RID
license=$LICENSE
linkage=$LINKAGE
deps=${DEPS:-none}
built_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
host=$(uname -srm)
EOF

log "=== staged -> $OUT_RID ==="
ls -la "$OUT_RID/lib"
