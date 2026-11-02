#!/usr/bin/env bash
# Packs the managed, native-asset, and meta NuGet packages.
#
# Inputs (environment):
#   PACKAGE_VERSION   NuGet version to stamp (default 9.0.1-preview.1)
#   NATIVE_DIR        root of extracted native artifacts: <license>/<rid>/lib/<files>
#                     (default: artifacts/native)
#   OUT_DIR           output folder for .nupkg (default: artifacts/packages)
#   MANAGED_TFMS      optional override for the managed package TargetFrameworks
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"

VERSION="${PACKAGE_VERSION:-9.0.1-preview.1}"
NATIVE_DIR="${NATIVE_DIR:-$REPO/artifacts/native}"
OUT="${OUT_DIR:-$REPO/artifacts/packages}"
mkdir -p "$OUT"

log() { printf '\033[1;35m[pack]\033[0m %s\n' "$*" >&2; }

# rid -> packaging family folder (desktop only; mobile is packaged separately).
family_of() {
  case "$1" in
    win-*)   echo Windows ;;
    linux-*) echo Linux ;;
    osx-*)   echo macOS ;;
    *)       echo "" ;;
  esac
}

pack_managed() {
  local tfm_arg=()
  [ -n "${MANAGED_TFMS:-}" ] && tfm_arg=(-p:TargetFrameworks="$MANAGED_TFMS")
  log "packing managed FFmpegSharpApi $VERSION"
  dotnet pack "$REPO/src/FFmpegSharpApi/FFmpegSharpApi.csproj" -c Release -o "$OUT" \
    -p:PackageVersion="$VERSION" "${tfm_arg[@]}"
}

pack_native_family() {
  local license="$1" family="$2" stage="$3"
  local suffix="" lic="LGPL-3.0-or-later"
  [ "$license" = gpl ] && { suffix=".GPL"; lic="GPL-3.0-or-later"; }
  log "packing FFmpegSharpApi.Native.$family$suffix ($license)"
  dotnet pack "$REPO/packaging/FFmpegSharpApi.NativeAsset/FFmpegSharpApi.NativeAsset.csproj" \
    -c Release -o "$OUT" \
    -p:PackageId="FFmpegSharpApi.Native.$family$suffix" \
    -p:PackageVersion="$VERSION" \
    -p:PackageLicenseExpression="$lic" \
    -p:Description="Native FFmpeg 9.0 ($( [ "$license" = gpl ] && echo 'GPL v3' || echo 'LGPL v3' )) shared libraries for $family." \
    -p:NativeRoot="$stage/$family"
}

pack_native() {
  local license="$1"
  [ -d "$NATIVE_DIR/$license" ] || { log "no native artifacts for $license, skipping"; return 0; }
  local stage="$REPO/build-work/pack/$license"
  rm -rf "$stage"; mkdir -p "$stage"

  shopt -s nullglob
  local any=0
  for ridpath in "$NATIVE_DIR/$license"/*; do
    [ -d "$ridpath/lib" ] || continue
    local rid family
    rid="$(basename "$ridpath")"
    family="$(family_of "$rid")"
    [ -n "$family" ] || continue
    mkdir -p "$stage/$family/$rid/native"
    cp "$ridpath/lib/"* "$stage/$family/$rid/native/"
    any=1
  done

  for family in Windows Linux macOS; do
    [ -d "$stage/$family" ] && pack_native_family "$license" "$family" "$stage"
  done

  if [ "$any" = 1 ]; then
    local suffix="" lic="LGPL-3.0-or-later" id="FFmpegSharpApi.Native"
    [ "$license" = gpl ] && { suffix=".GPL"; lic="GPL-3.0-or-later"; id="FFmpegSharpApi.Native.GPL"; }
    log "packing meta $id ($license)"
    dotnet pack "$REPO/packaging/FFmpegSharpApi.Native/FFmpegSharpApi.Native.csproj" \
      -c Release -o "$OUT" \
      -p:PackageId="$id" \
      -p:PackageVersion="$VERSION" \
      -p:DependencyVersion="$VERSION" \
      -p:PackageLicenseExpression="$lic" \
      -p:NativeFlavorSuffix="$suffix"
  fi
}

pack_managed
pack_native lgpl
pack_native gpl

log "packages in $OUT:"
ls -1 "$OUT"/*.nupkg 2>/dev/null || true
