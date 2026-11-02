#!/usr/bin/env bash
# Shared helpers for the native FFmpeg build scripts.
# Source this file; do not execute it.

log()  { printf '\033[1;34m[ffbuild]\033[0m %s\n' "$*" >&2; }
warn() { printf '\033[1;33m[ffbuild][warn]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[ffbuild][error]\033[0m %s\n' "$*" >&2; exit 1; }
run()  { log "+ $*"; "$@"; }

ncpu() {
  if command -v nproc >/dev/null 2>&1; then nproc
  elif command -v sysctl >/dev/null 2>&1; then sysctl -n hw.ncpu
  else echo 4
  fi
}

# clone_component <name> <repo> <ref> <destdir>
# Shallow-clones a branch or tag; falls back to a full clone for raw commit SHAs.
clone_component() {
  local name="$1" repo="$2" ref="$3" dest="$4"
  if [ -d "$dest/.git" ]; then
    log "$name: reusing existing clone at $dest"
    return 0
  fi
  log "$name: cloning $repo @ $ref"
  if git clone --depth 1 --branch "$ref" --recurse-submodules "$repo" "$dest" 2>/dev/null; then
    return 0
  fi
  warn "$name: shallow clone of ref '$ref' failed, retrying with a full clone"
  run git clone --recurse-submodules "$repo" "$dest"
  run git -C "$dest" checkout --detach "$ref"
  run git -C "$dest" submodule update --init --recursive
}

# apply_patches <component> <srcdir> <patchroot>
# Applies patches/<component>/*.patch in lexical order, tolerating already-applied patches.
apply_patches() {
  local comp="$1" src="$2" root="$3"
  local dir="$root/$comp"
  [ -d "$dir" ] || { log "$comp: no patch directory, skipping"; return 0; }
  shopt -s nullglob
  local applied=0 p
  for p in "$dir"/*.patch; do
    if git -C "$src" apply --check --whitespace=nowarn "$p" 2>/dev/null; then
      run git -C "$src" apply --whitespace=nowarn "$p"
      applied=$((applied + 1))
    elif git -C "$src" apply --check --reverse --whitespace=nowarn "$p" 2>/dev/null; then
      log "$comp: patch already applied: $(basename "$p")"
    else
      die "$comp: patch failed to apply: $p"
    fi
  done
  shopt -u nullglob
  log "$comp: applied $applied patch(es)"
}

# strip_binary <strip-tool> <file>
strip_binary() {
  local strip="$1" file="$2"
  [ -n "$strip" ] && [ -x "$(command -v "${strip%% *}" || true)" ] || return 0
  "$strip" -x "$file" 2>/dev/null || "$strip" "$file" 2>/dev/null || true
}
