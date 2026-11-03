#!/usr/bin/env bash
# Drives the MAUI test .app on a freshly created iOS simulator: boots it, installs the app,
# launches it attached to the console (the in-app runner self-exits when finished), then
# asserts the completion marker. Runs as a single shell so state survives the whole sequence.
set -eo pipefail

BUNDLE=com.companyname.ffmpegsharpapi.tests.maui
WS="${GITHUB_WORKSPACE:-$PWD}"

APP="$(find "$WS/Tests/FFmpegSharpApi.Tests.Maui/bin" -name '*.app' -type d | head -n1)"
if [ -z "$APP" ]; then
  echo "No .app bundle found under Tests/FFmpegSharpApi.Tests.Maui/bin" >&2
  exit 1
fi
echo "App bundle: $APP"

RUNTIME="$(xcrun simctl list runtimes ios -j | python3 -c 'import sys,json; r=[x for x in json.load(sys.stdin)["runtimes"] if x["isAvailable"]]; print(r[-1]["identifier"])')"
DEVTYPE="$(xcrun simctl list devicetypes -j | python3 -c 'import sys,json; d=[x for x in json.load(sys.stdin)["devicetypes"] if "iPhone" in x["name"]]; print(d[-1]["identifier"])')"
echo "Runtime=$RUNTIME DeviceType=$DEVTYPE"

UDID="$(xcrun simctl create ffmpeg-test "$DEVTYPE" "$RUNTIME")"
echo "Created simulator $UDID"
trap 'xcrun simctl shutdown "$UDID" >/dev/null 2>&1 || true; xcrun simctl delete "$UDID" >/dev/null 2>&1 || true' EXIT

xcrun simctl boot "$UDID"
xcrun simctl bootstatus "$UDID" -b

xcrun simctl install "$UDID" "$APP"

echo "Launching $BUNDLE (console attached; the app self-exits when done)..."
set +e
OUT="$(xcrun simctl launch --console-pty "$UDID" "$BUNDLE" 2>&1)"
set -e

echo "---- markers ----"
echo "$OUT" | grep "##MOBILE_" || true
echo "---- FFmpeg / errors ----"
echo "$OUT" | grep -iE "FFmpeg |Unhandled|Exception|not loaded|dyld|Symbol not found" | head -n 40 || true

echo "$OUT" | grep -q "##MOBILE_DONE## PASS"
