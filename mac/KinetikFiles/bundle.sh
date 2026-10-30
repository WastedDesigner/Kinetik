#!/bin/bash
# Builds Kinetik Files and wraps it into "Kinetik Files.app" in ./build.
#   ./bundle.sh            release build, ad-hoc signed (runs on this Mac)
#   ./bundle.sh debug      debug build
#   SIGN_ID="Developer ID Application: …" ./bundle.sh   signed for distribution
set -euo pipefail
cd "$(dirname "$0")"

CONFIG="${1:-release}"
swift build -c "$CONFIG" --arch arm64 --arch x86_64 2>/dev/null || swift build -c "$CONFIG"
BIN="$(swift build -c "$CONFIG" --show-bin-path)/KinetikFiles"

APP="build/Kinetik Files.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/KinetikFiles"
cp Resources/Info.plist "$APP/Contents/Info.plist"

# The app icon, from the suite's icon.png at the top of the repo.
ICON_SRC="../../icon.png"
if [ -f "$ICON_SRC" ]; then
  SET="$(mktemp -d)/AppIcon.iconset"
  mkdir -p "$SET"
  for s in 16 32 128 256 512; do
    sips -z $s $s "$ICON_SRC" --out "$SET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) "$ICON_SRC" --out "$SET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$SET" -o "$APP/Contents/Resources/AppIcon.icns"
fi

codesign --force --deep --options runtime --sign "${SIGN_ID:--}" "$APP"
echo "Built $APP"
