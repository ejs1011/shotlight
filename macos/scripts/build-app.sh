#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
TASK_CACHE="$PWD/.build/module-cache"
export CLANG_MODULE_CACHE_PATH="$TASK_CACHE"
export SWIFTPM_MODULECACHE_OVERRIDE="$TASK_CACHE"
mkdir -p "$PWD/.build/local"
swiftc -O -target arm64-apple-macosx13.0 -module-cache-path "$TASK_CACHE" -framework AppKit -framework Carbon -framework ScreenCaptureKit Sources/Shotlight/*.swift -o "$PWD/.build/local/Shotlight"
TASK_BIN="$PWD/.build/local"
TASK_APP="$(mktemp -d /private/tmp/shotlight-build.XXXXXX)/Shotlight.app"
mkdir -p "$TASK_APP/Contents/MacOS"
cp "$TASK_BIN/Shotlight" "$TASK_APP/Contents/MacOS/Shotlight"
cat > "$TASK_APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Shotlight</string>
<key>CFBundleIdentifier</key><string>local.shotlight.app</string>
<key>CFBundleName</key><string>Shotlight</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.5.0</string>
<key>CFBundleVersion</key><string>5</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
xattr -cr "$TASK_APP"
codesign --force --sign - "$TASK_APP"
mkdir -p "$PWD/artifacts"
ditto "$TASK_APP" "$PWD/artifacts/Shotlight.app"
echo "Built $PWD/artifacts/Shotlight.app (staged at $TASK_APP)"
