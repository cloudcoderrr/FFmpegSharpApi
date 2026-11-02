# Source patches

Unified-diff patches applied to the upstream source trees during the native
build (`build-native` workflow). The build clones each upstream project at the
pinned ref in [`build/ffmpeg/versions.env`](../build/ffmpeg/versions.env) and
then applies every patch for that component with `git apply`, in lexical order.

## Layout

```
patches/
  ffmpeg/    patches applied on top of the FFmpeg source tree
  dav1d/     patches applied on top of the dav1d source tree
  x264/      patches applied on top of the x264 source tree
  x265/      patches applied on top of the x265 source tree
  libxml2/   patches applied on top of the libxml2 source tree
```

A component folder may be empty. Patches are optional.

## File format

* One logical change per file, named `NNNN-short-description.patch`, e.g.
  `0001-fix-arm64-neon-detection.patch`.
* Must be a standard unified diff with `a/` and `b/` prefixes so that
  `git apply -p1` works, for example produced by `git diff` or
  `git format-patch`.
* Paths inside the patch are relative to the **root of the component source
  tree**, not to this repository.

## Creating a patch

```bash
git clone https://github.com/FFmpeg/FFmpeg.git
cd FFmpeg
git checkout n9.0.1
# ... edit files ...
git diff > ../patches/ffmpeg/0001-my-change.patch
```

## How patches are applied in CI

The build helper runs, for each component:

```bash
for p in patches/<component>/*.patch; do
  git -C "$srcdir" apply --verbose --whitespace=nowarn "$p"
done
```

If a patch fails to apply the build fails fast, so keep patches rebased against
the pinned upstream ref.
