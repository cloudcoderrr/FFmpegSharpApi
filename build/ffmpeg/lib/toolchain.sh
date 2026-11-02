#!/usr/bin/env bash
# Resolves the cross-compilation toolchain and FFmpeg configure flags for a RID.
# Source this file and call `configure_toolchain` after exporting RID.
#
# Required environment per family:
#   windows  -> LLVM_MINGW_ROOT   (root of an llvm-mingw toolchain)
#   android  -> ANDROID_NDK_ROOT  (root of an Android NDK, r26+)
#   apple    -> a macOS host with Xcode command line tools
#
# After `configure_toolchain` the following are set for consumers:
#   FAMILY ARCH LINKAGE HOST_TRIPLE SYSROOT
#   CC CXX AR RANLIB STRIP NM
#   TOOL_CFLAGS TOOL_LDFLAGS
#   FF_ARCH FF_TARGET_OS FF_CROSS_PREFIX FF_CROSS_COMPILE
#   FF_EXTRA_CONFIGURE (bash array)
#   MESON_SYSTEM MESON_CPU_FAMILY MESON_CPU CMAKE_SYSTEM_NAME CMAKE_SYSTEM_PROCESSOR

configure_toolchain() {
  : "${RID:?RID must be set before calling configure_toolchain}"
  FAMILY=""; ARCH=""; LINKAGE="shared"
  CC=""; CXX=""; AR=""; RANLIB=""; STRIP=""; NM=""
  SYSROOT=""; HOST_TRIPLE=""
  TOOL_CFLAGS=""; TOOL_LDFLAGS=""
  FF_ARCH=""; FF_TARGET_OS=""; FF_CROSS_PREFIX=""; FF_CROSS_COMPILE=0
  FF_EXTRA_CONFIGURE=()
  MESON_SYSTEM=""; MESON_CPU_FAMILY=""; MESON_CPU=""
  CMAKE_SYSTEM_NAME=""; CMAKE_SYSTEM_PROCESSOR=""
  APPLE=0; APPLE_TARGET=""; APPLE_MIN=""

  case "$RID" in
    win-x64)   FAMILY=windows; ARCH=x86_64; FF_ARCH=x86_64; WIN_TRIPLE=x86_64-w64-mingw32 ;;
    win-x86)   FAMILY=windows; ARCH=x86;    FF_ARCH=x86;    WIN_TRIPLE=i686-w64-mingw32 ;;
    win-arm64) FAMILY=windows; ARCH=arm64;  FF_ARCH=aarch64; WIN_TRIPLE=aarch64-w64-mingw32 ;;
    linux-x64)   FAMILY=linux; ARCH=x86_64; FF_ARCH=x86_64 ;;
    linux-x86)   FAMILY=linux; ARCH=x86;    FF_ARCH=x86 ;;
    linux-arm64) FAMILY=linux; ARCH=arm64;  FF_ARCH=aarch64 ;;
    linux-arm)   FAMILY=linux; ARCH=arm;    FF_ARCH=arm ;;
    osx-x64)     FAMILY=macos; ARCH=x86_64; FF_ARCH=x86_64 ;;
    osx-arm64)   FAMILY=macos; ARCH=arm64;  FF_ARCH=arm64 ;;
    maccatalyst-x64)    FAMILY=maccatalyst; ARCH=x86_64; FF_ARCH=x86_64; LINKAGE=static ;;
    maccatalyst-arm64)  FAMILY=maccatalyst; ARCH=arm64;  FF_ARCH=arm64;  LINKAGE=static ;;
    ios-arm64)          FAMILY=ios;    ARCH=arm64;  FF_ARCH=arm64;  LINKAGE=static ;;
    iossimulator-x64)   FAMILY=iossim; ARCH=x86_64; FF_ARCH=x86_64; LINKAGE=static ;;
    iossimulator-arm64) FAMILY=iossim; ARCH=arm64;  FF_ARCH=arm64;  LINKAGE=static ;;
    android-arm)   FAMILY=android; ARCH=arm;    FF_ARCH=arm ;;
    android-arm64) FAMILY=android; ARCH=arm64;  FF_ARCH=aarch64 ;;
    android-x86)   FAMILY=android; ARCH=x86;    FF_ARCH=x86 ;;
    android-x64)   FAMILY=android; ARCH=x86_64; FF_ARCH=x86_64 ;;
    *) die "Unknown RID: $RID" ;;
  esac

  # meson/cmake CPU descriptors shared by all families.
  case "$ARCH" in
    x86_64) MESON_CPU_FAMILY=x86_64; MESON_CPU=x86_64; CMAKE_SYSTEM_PROCESSOR=x86_64 ;;
    x86)    MESON_CPU_FAMILY=x86;    MESON_CPU=i686;   CMAKE_SYSTEM_PROCESSOR=x86 ;;
    arm64)  MESON_CPU_FAMILY=aarch64; MESON_CPU=aarch64; CMAKE_SYSTEM_PROCESSOR=aarch64 ;;
    arm)    MESON_CPU_FAMILY=arm;    MESON_CPU=armv7;  CMAKE_SYSTEM_PROCESSOR=armv7-a ;;
  esac

  case "$FAMILY" in
    windows) _tc_windows ;;
    linux)   _tc_linux ;;
    android) _tc_android ;;
    macos|maccatalyst|ios|iossim) _tc_apple ;;
  esac

  if [ "$LINKAGE" = "static" ]; then
    FF_EXTRA_CONFIGURE+=(--enable-static --disable-shared)
  else
    FF_EXTRA_CONFIGURE+=(--enable-shared --disable-static)
  fi
  FF_EXTRA_CONFIGURE+=(--enable-pic)
}

_tc_windows() {
  local root="${LLVM_MINGW_ROOT:?LLVM_MINGW_ROOT must point to an llvm-mingw toolchain}"
  export PATH="$root/bin:$PATH"
  CC="$WIN_TRIPLE-clang"; CXX="$WIN_TRIPLE-clang++"
  AR="$WIN_TRIPLE-ar"; RANLIB="$WIN_TRIPLE-ranlib"; STRIP="$WIN_TRIPLE-strip"; NM="$WIN_TRIPLE-nm"
  HOST_TRIPLE="$WIN_TRIPLE"
  FF_TARGET_OS=mingw32
  FF_CROSS_PREFIX="$WIN_TRIPLE-"
  FF_CROSS_COMPILE=1
  MESON_SYSTEM=windows; CMAKE_SYSTEM_NAME=Windows
  FF_EXTRA_CONFIGURE+=(--cc="$CC" --cxx="$CXX")
}

_tc_linux() {
  FF_TARGET_OS=linux
  MESON_SYSTEM=linux; CMAKE_SYSTEM_NAME=Linux
  case "$RID" in
    linux-x64)
      CC=gcc; CXX=g++; AR=ar; RANLIB=ranlib; STRIP=strip; NM=nm
      HOST_TRIPLE=x86_64-linux-gnu; FF_CROSS_COMPILE=0 ;;
    linux-x86)
      CC=gcc; CXX=g++; AR=ar; RANLIB=ranlib; STRIP=strip; NM=nm
      TOOL_CFLAGS="-m32"; TOOL_LDFLAGS="-m32"
      HOST_TRIPLE=i686-linux-gnu; FF_CROSS_COMPILE=1
      FF_EXTRA_CONFIGURE+=(--extra-cflags="-m32" --extra-ldflags="-m32") ;;
    linux-arm64)
      HOST_TRIPLE=aarch64-linux-gnu
      CC=$HOST_TRIPLE-gcc; CXX=$HOST_TRIPLE-g++; AR=$HOST_TRIPLE-ar
      RANLIB=$HOST_TRIPLE-ranlib; STRIP=$HOST_TRIPLE-strip; NM=$HOST_TRIPLE-nm
      FF_CROSS_PREFIX="$HOST_TRIPLE-"; FF_CROSS_COMPILE=1 ;;
    linux-arm)
      HOST_TRIPLE=arm-linux-gnueabihf
      CC=$HOST_TRIPLE-gcc; CXX=$HOST_TRIPLE-g++; AR=$HOST_TRIPLE-ar
      RANLIB=$HOST_TRIPLE-ranlib; STRIP=$HOST_TRIPLE-strip; NM=$HOST_TRIPLE-nm
      FF_CROSS_PREFIX="$HOST_TRIPLE-"; FF_CROSS_COMPILE=1 ;;
  esac
  FF_EXTRA_CONFIGURE+=(--cc="$CC" --cxx="$CXX")
}

_tc_android() {
  local ndk="${ANDROID_NDK_ROOT:?ANDROID_NDK_ROOT must be set for android builds}"
  local host
  case "$(uname -s)" in
    Linux)  host=linux-x86_64 ;;
    Darwin) host=darwin-x86_64 ;;
    *) die "Unsupported Android build host: $(uname -s)" ;;
  esac
  local tc="$ndk/toolchains/llvm/prebuilt/$host"
  [ -d "$tc" ] || die "NDK toolchain not found at $tc"
  export PATH="$tc/bin:$PATH"
  local api=21 ct
  case "$RID" in
    android-arm)   ct=armv7a-linux-androideabi$api; FF_EXTRA_CONFIGURE+=(--cpu=armv7-a) ;;
    android-arm64) ct=aarch64-linux-android$api ;;
    android-x86)   ct=i686-linux-android$api; FF_EXTRA_CONFIGURE+=(--disable-asm) ;;
    android-x64)   ct=x86_64-linux-android$api ;;
  esac
  CC="$tc/bin/${ct}-clang"; CXX="$tc/bin/${ct}-clang++"
  AR="$tc/bin/llvm-ar"; RANLIB="$tc/bin/llvm-ranlib"; STRIP="$tc/bin/llvm-strip"; NM="$tc/bin/llvm-nm"
  SYSROOT="$tc/sysroot"
  HOST_TRIPLE="$(basename "$CC" | sed "s/${api}-clang\$//")"
  FF_TARGET_OS=android
  FF_CROSS_COMPILE=1
  MESON_SYSTEM=android; CMAKE_SYSTEM_NAME=Linux
  FF_EXTRA_CONFIGURE+=(--cc="$CC" --cxx="$CXX" --ar="$AR" --ranlib="$RANLIB"
    --strip="$STRIP" --nm="$NM" --sysroot="$SYSROOT")
}

_tc_apple() {
  local sdk target minver=""
  case "$FAMILY" in
    macos)       sdk=macosx ;;
    maccatalyst) sdk=macosx ;;
    ios)         sdk=iphoneos ;;
    iossim)      sdk=iphonesimulator ;;
  esac
  SYSROOT="$(xcrun --sdk "$sdk" --show-sdk-path)"
  CC="$(xcrun --sdk "$sdk" --find clang)"
  CXX="$(xcrun --sdk "$sdk" --find clang++)"
  AR="$(xcrun --sdk "$sdk" --find ar)"
  RANLIB="$(xcrun --sdk "$sdk" --find ranlib)"
  STRIP="$(xcrun --sdk "$sdk" --find strip)"
  NM="$(xcrun --sdk "$sdk" --find nm)"
  case "$FAMILY" in
    macos)
      target="${ARCH}-apple-macos"
      if [ "$ARCH" = arm64 ]; then minver="-mmacosx-version-min=11.0"; else minver="-mmacosx-version-min=10.15"; fi
      MESON_SYSTEM=darwin; CMAKE_SYSTEM_NAME=Darwin ;;
    maccatalyst)
      target="${ARCH}-apple-ios14.0-macabi"
      MESON_SYSTEM=darwin; CMAKE_SYSTEM_NAME=Darwin; APPLE=1 ;;
    ios)
      target="${ARCH}-apple-ios13.0"
      MESON_SYSTEM=darwin; CMAKE_SYSTEM_NAME=Darwin; APPLE=1 ;;
    iossim)
      target="${ARCH}-apple-ios13.0-simulator"
      MESON_SYSTEM=darwin; CMAKE_SYSTEM_NAME=Darwin; APPLE=1 ;;
  esac
  APPLE_TARGET="$target"; APPLE_MIN="$minver"
  HOST_TRIPLE="${ARCH}-apple-darwin"
  TOOL_CFLAGS="-arch $ARCH -target $target -isysroot $SYSROOT $minver"
  TOOL_LDFLAGS="-arch $ARCH -target $target -isysroot $SYSROOT $minver"
  FF_TARGET_OS=darwin
  FF_CROSS_COMPILE=1
  FF_EXTRA_CONFIGURE+=(--cc="$CC" --cxx="$CXX" --as="$CC" --sysroot="$SYSROOT"
    --extra-cflags="$TOOL_CFLAGS" --extra-cxxflags="$TOOL_CFLAGS" --extra-ldflags="$TOOL_LDFLAGS")
}

# emit_meson_cross <file> : write a meson cross file for the current toolchain.
emit_meson_cross() {
  local f="$1"
  local cc_bin="${CC%% *}" extra_c=""
  [ -n "$TOOL_CFLAGS" ] && extra_c="$TOOL_CFLAGS"
  {
    echo "[binaries]"
    echo "c = '$cc_bin'"
    echo "cpp = '${CXX%% *}'"
    echo "ar = '$AR'"
    echo "strip = '$STRIP'"
    echo "pkg-config = 'pkg-config'"
    echo ""
    echo "[host_machine]"
    echo "system = '$MESON_SYSTEM'"
    echo "cpu_family = '$MESON_CPU_FAMILY'"
    echo "cpu = '$MESON_CPU'"
    echo "endian = 'little'"
    if [ -n "$TOOL_CFLAGS" ] || [ -n "$TOOL_LDFLAGS" ]; then
      echo ""
      echo "[built-in options]"
      [ -n "$TOOL_CFLAGS" ] && printf "c_args = ['%s']\n" "$(echo "$TOOL_CFLAGS" | sed "s/ /','/g")"
      [ -n "$TOOL_LDFLAGS" ] && printf "c_link_args = ['%s']\n" "$(echo "$TOOL_LDFLAGS" | sed "s/ /','/g")"
    fi
  } > "$f"
}

# emit_cmake_toolchain <file> : write a cmake toolchain file for the current toolchain.
emit_cmake_toolchain() {
  local f="$1"
  {
    echo "set(CMAKE_SYSTEM_NAME $CMAKE_SYSTEM_NAME)"
    echo "set(CMAKE_SYSTEM_PROCESSOR $CMAKE_SYSTEM_PROCESSOR)"
    echo "set(CMAKE_C_COMPILER ${CC%% *})"
    echo "set(CMAKE_CXX_COMPILER ${CXX%% *})"
    echo "set(CMAKE_AR $AR)"
    echo "set(CMAKE_RANLIB $RANLIB)"
    if [ "${APPLE:-0}" = 1 ]; then
      echo "set(CMAKE_OSX_SYSROOT $SYSROOT)"
      echo "set(CMAKE_OSX_ARCHITECTURES $ARCH)"
      echo "set(CMAKE_C_FLAGS_INIT \"-target $APPLE_TARGET $APPLE_MIN\")"
      echo "set(CMAKE_CXX_FLAGS_INIT \"-target $APPLE_TARGET $APPLE_MIN\")"
    else
      [ -n "$SYSROOT" ] && echo "set(CMAKE_SYSROOT $SYSROOT)"
      [ -n "$TOOL_CFLAGS" ] && echo "set(CMAKE_C_FLAGS_INIT \"$TOOL_CFLAGS\")"
      [ -n "$TOOL_CFLAGS" ] && echo "set(CMAKE_CXX_FLAGS_INIT \"$TOOL_CFLAGS\")"
    fi
    echo "set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)"
    echo "set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)"
    echo "set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)"
  } > "$f"
}
