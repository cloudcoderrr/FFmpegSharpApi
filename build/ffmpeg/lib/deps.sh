#!/usr/bin/env bash
# Builds external codec/support libraries into $PREFIX as static, PIC-enabled
# libraries that FFmpeg then links. Source after lib/toolchain.sh.
#
# Each library is only built when listed in $DEPS. Expected variables:
#   WORK PREFIX REPO_ROOT LICENSE  and the toolchain vars from configure_toolchain.

# build_dep <name>
build_dep() {
  case "$1" in
    dav1d)   _dep_dav1d ;;
    libxml2) _dep_libxml2 ;;
    x264)    _dep_x264 ;;
    x265)    _dep_x265 ;;
    *) die "Unknown dependency: $1" ;;
  esac
}

# ffmpeg_dep_flags : echoes the FFmpeg --enable-* flags for the enabled deps.
ffmpeg_dep_flags() {
  local d
  for d in $DEPS; do
    case "$d" in
      dav1d)   echo "--enable-libdav1d" ;;
      libxml2) echo "--enable-libxml2" ;;
      x264)    echo "--enable-libx264" ;;
      x265)    echo "--enable-libx265" ;;
    esac
  done
}

_cross_file_args() {
  # echoes meson --cross-file argument when cross compiling
  if [ "$FF_CROSS_COMPILE" = 1 ]; then
    local f="$WORK/meson-cross-$RID.txt"
    emit_meson_cross "$f"
    echo "--cross-file $f"
  fi
}

_dep_dav1d() {
  clone_component dav1d "$DAV1D_REPO" "$DAV1D_REF" "$WORK/dav1d"
  apply_patches dav1d "$WORK/dav1d" "$REPO_ROOT/patches"
  rm -rf "$WORK/dav1d/_build"
  # shellcheck disable=SC2046
  run meson setup "$WORK/dav1d/_build" "$WORK/dav1d" \
    --prefix="$PREFIX" --libdir=lib --buildtype=release \
    --default-library=static -Db_staticpic=true \
    -Denable_tools=false -Denable_tests=false $(_cross_file_args)
  run ninja -C "$WORK/dav1d/_build" -j"$(ncpu)"
  run ninja -C "$WORK/dav1d/_build" install
}

_dep_libxml2() {
  clone_component libxml2 "$LIBXML2_REPO" "$LIBXML2_REF" "$WORK/libxml2"
  apply_patches libxml2 "$WORK/libxml2" "$REPO_ROOT/patches"
  local tc="$WORK/cmake-$RID.cmake"; emit_cmake_toolchain "$tc"
  rm -rf "$WORK/libxml2/_build"
  run cmake -S "$WORK/libxml2" -B "$WORK/libxml2/_build" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$tc" -DCMAKE_INSTALL_PREFIX="$PREFIX" \
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DBUILD_SHARED_LIBS=OFF \
    -DLIBXML2_WITH_ICONV=OFF -DLIBXML2_WITH_LZMA=OFF -DLIBXML2_WITH_ZLIB=OFF \
    -DLIBXML2_WITH_PYTHON=OFF -DLIBXML2_WITH_PROGRAMS=OFF -DLIBXML2_WITH_TESTS=OFF \
    -DLIBXML2_WITH_HTTP=OFF -DLIBXML2_WITH_FTP=OFF -DLIBXML2_WITH_CATALOG=OFF
  run cmake --build "$WORK/libxml2/_build" -j"$(ncpu)"
  run cmake --install "$WORK/libxml2/_build"
}

_dep_x264() {
  clone_component x264 "$X264_REPO" "$X264_REF" "$WORK/x264"
  apply_patches x264 "$WORK/x264" "$REPO_ROOT/patches"
  local hostflag=() crossflag=()
  [ -n "$HOST_TRIPLE" ] && [ "$FF_CROSS_COMPILE" = 1 ] && hostflag=(--host="$HOST_TRIPLE")
  [ -n "$FF_CROSS_PREFIX" ] && crossflag=(--cross-prefix="$FF_CROSS_PREFIX")
  ( cd "$WORK/x264"
    CC="$CC" CXX="$CXX" AR="$AR" RANLIB="$RANLIB" STRIP="$STRIP" \
    CFLAGS="$TOOL_CFLAGS" LDFLAGS="$TOOL_LDFLAGS" \
    ./configure --prefix="$PREFIX" --enable-static --enable-pic \
      --disable-cli --disable-opencl "${hostflag[@]}" "${crossflag[@]}"
    make -j"$(ncpu)"
    make install )
}

_dep_x265() {
  clone_component x265 "$X265_REPO" "$X265_REF" "$WORK/x265"
  apply_patches x265 "$WORK/x265" "$REPO_ROOT/patches"
  local tc="$WORK/cmake-$RID.cmake"; emit_cmake_toolchain "$tc"
  rm -rf "$WORK/x265/_build"
  local extra=()
  case "$ARCH" in arm|arm64) extra+=(-DCROSS_COMPILE_ARM=ON -DENABLE_ASSEMBLY=OFF) ;; esac
  run cmake -S "$WORK/x265/source" -B "$WORK/x265/_build" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$tc" -DCMAKE_INSTALL_PREFIX="$PREFIX" \
    -DCMAKE_BUILD_TYPE=Release -DENABLE_SHARED=OFF -DENABLE_CLI=OFF \
    -DENABLE_PIC=ON "${extra[@]}"
  run cmake --build "$WORK/x265/_build" -j"$(ncpu)"
  run cmake --install "$WORK/x265/_build"
  # FFmpeg discovers x265 via pkg-config; ensure the .pc advertises the stdc++ dep.
  if [ -f "$PREFIX/lib/pkgconfig/x265.pc" ]; then
    sed -i.bak 's/^Libs.private:.*/&/' "$PREFIX/lib/pkgconfig/x265.pc" 2>/dev/null || true
  fi
}
