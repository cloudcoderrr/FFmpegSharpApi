#!/usr/bin/env bash
# Drives the MAUI test APK on a booted Android emulator: installs it, launches it,
# waits for the in-app runner to print its completion marker, then asserts PASS.
# Runs as a single shell (invoked once) so variables and the background logcat
# capture survive for the whole sequence.
set -eo pipefail

PKG=com.companyname.ffmpegsharpapi.tests.maui
WS="${GITHUB_WORKSPACE:-$PWD}"
LOG="$WS/logcat.txt"

APK="$(find "$WS/Tests/FFmpegSharpApi.Tests.Maui/bin" -name '*-Signed.apk' | head -n1)"
if [ -z "$APK" ]; then
  echo "No signed APK found under Tests/FFmpegSharpApi.Tests.Maui/bin" >&2
  exit 1
fi
echo "Installing $APK"
adb install -r "$APK"

adb logcat -c
adb logcat > "$LOG" 2>&1 &
LOGPID=$!

echo "Launching $PKG"
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1

echo "Waiting for the test run to finish (up to 15 min)..."
for _ in $(seq 1 180); do
  if grep -q "##MOBILE_DONE##" "$LOG"; then break; fi
  sleep 5
done
kill "$LOGPID" 2>/dev/null || true

echo "---- markers ----"
grep "##MOBILE_" "$LOG" || true
echo "---- FFmpeg / errors ----"
grep -iE "FFmpeg |Unhandled|Exception|not loaded|UnsatisfiedLink|dlopen|avcodec" "$LOG" | head -n 40 || true

grep -q "##MOBILE_DONE## PASS" "$LOG"
