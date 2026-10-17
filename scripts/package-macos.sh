#!/usr/bin/env bash
#
# Builds a macOS .app bundle and a compressed .dmg from a self-contained
# dotnet publish output directory.
#
# Usage:
#   scripts/package-macos.sh <version> <osx-arm64|osx-x64> <publish-dir> <output-dir>
#
# The resulting file is written to:
#   <output-dir>/VideoLuwak-<version>-macos-<arch>.dmg
#
# The bundle is ad-hoc signed (codesign --sign -) because release builds do not
# have an Apple Developer ID / notarization profile. Users may still need to
# right-click > Open the first time. The app expects ffmpeg/ffprobe on PATH.
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <osx-arm64|osx-x64> <publish-dir> <output-dir>" >&2
  exit 2
fi

VERSION="$1"
RID="$2"
PUBLISH_DIR="$3"
OUTPUT_DIR="$4"

if [[ ! "${RID}" =~ ^osx-(arm64|x64)$ ]]; then
  echo "error: RID must be osx-arm64 or osx-x64 (got '${RID}')." >&2
  exit 2
fi

if [[ ! -d "${PUBLISH_DIR}" ]]; then
  echo "error: publish directory does not exist: ${PUBLISH_DIR}" >&2
  exit 1
fi

if ! command -v hdiutil >/dev/null 2>&1; then
  echo "error: hdiutil is required to build a DMG (run this on macOS)." >&2
  exit 1
fi

APP_NAME="VideoLuwak"
ARCH="${RID#osx-}"
OUTPUT_DIR="$(cd "${OUTPUT_DIR}" 2>/dev/null && pwd || (mkdir -p "${OUTPUT_DIR}" && cd "${OUTPUT_DIR}" && pwd))"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/videoluwak-dmg.XXXXXX")"
trap 'rm -rf "${WORK_DIR}"' EXIT

APP_BUNDLE="${WORK_DIR}/${APP_NAME}.app"
CONTENTS="${APP_BUNDLE}/Contents"
MACOS_DIR="${CONTENTS}/MacOS"
RESOURCES_DIR="${CONTENTS}/Resources"
DMG_PATH="${OUTPUT_DIR}/${APP_NAME}-${VERSION}-macos-${ARCH}.dmg"

mkdir -p "${MACOS_DIR}" "${RESOURCES_DIR}"

# Copy the complete self-contained publish output into the app bundle. On macOS
# we intentionally publish non-single-file so the bundle is a normal set of
# files rather than a self-extracting executable.
cp -R "${PUBLISH_DIR}/." "${MACOS_DIR}/"
chmod +x "${MACOS_DIR}/${APP_NAME}"

# Drop managed debugging symbols from the shipped bundle.
find "${MACOS_DIR}" -type f \( -name '*.pdb' -o -name '*.mdb' \) -delete 2>/dev/null || true

BUNDLE_VERSION="$(printf '%s' "${VERSION}" | sed -E 's/[^0-9]+/./g; s/^\.+//; s/\.+$//')"
if [[ -z "${BUNDLE_VERSION}" ]]; then
  BUNDLE_VERSION="0.0.0"
fi

cat > "${CONTENTS}/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleName</key>
  <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>
  <string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key>
  <string>com.videoluwak.app</string>
  <key>CFBundleExecutable</key>
  <string>${APP_NAME}</string>
  <key>CFBundleIconFile</key>
  <string>${APP_NAME}.icns</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
  <key>CFBundleVersion</key>
  <string>${BUNDLE_VERSION}</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.video</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
EOF

# Build a proper multi-resolution .icns from the master logo when the macOS
# tooling and the source asset are available.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ICON_SOURCE="${SCRIPT_DIR}/../src/VideoLuwak.App/Assets/logo/logo-1024.png"
if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1 && [[ -f "${ICON_SOURCE}" ]]; then
  ICONSET="${WORK_DIR}/${APP_NAME}.iconset"
  mkdir -p "${ICONSET}"

  make_icon() {
    sips -z "$1" "$1" "${ICON_SOURCE}" --out "${ICONSET}/$2" >/dev/null
  }

  make_icon 16   icon_16x16.png
  make_icon 32   icon_16x16@2x.png
  make_icon 32   icon_32x32.png
  make_icon 64   icon_32x32@2x.png
  make_icon 128  icon_128x128.png
  make_icon 256  icon_128x128@2x.png
  make_icon 256  icon_256x256.png
  make_icon 512  icon_256x256@2x.png
  make_icon 512  icon_512x512.png
  make_icon 1024 icon_512x512@2x.png

  iconutil -c icns "${ICONSET}" -o "${RESOURCES_DIR}/${APP_NAME}.icns"
fi

# Ad-hoc signature: keeps the bundle internally consistent on modern macOS even
# without a Developer ID. This is not notarization and does not remove Gatekeeper
# warnings for downloaded builds.
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "${APP_BUNDLE}" >/dev/null
fi

STAGE_DIR="${WORK_DIR}/dmg"
mkdir -p "${STAGE_DIR}"
cp -R "${APP_BUNDLE}" "${STAGE_DIR}/"
ln -s /Applications "${STAGE_DIR}/Applications"

for file in LICENSE THIRD_PARTY_NOTICES.md README.md; do
  if [[ -f "${SCRIPT_DIR}/../${file}" ]]; then
    cp "${SCRIPT_DIR}/../${file}" "${STAGE_DIR}/"
  fi
done

if [[ -d "${SCRIPT_DIR}/../LICENSES" ]]; then
  cp -R "${SCRIPT_DIR}/../LICENSES" "${STAGE_DIR}/"
fi

cat > "${STAGE_DIR}/README.txt" <<'EOF'
VideoLuwak
==========

VideoLuwak uses ffmpeg/ffprobe. Install them first, for example:

    brew install ffmpeg

Then drag VideoLuwak.app to Applications.

This build is ad-hoc signed but not notarized. If macOS blocks the first launch,
right-click (or Control-click) VideoLuwak.app and choose Open.

The graphical application is launched by opening VideoLuwak.app. The same
executable can be used from a terminal:

    /Applications/VideoLuwak.app/Contents/MacOS/VideoLuwak --help
EOF

hdiutil create \
  -volname "${APP_NAME} ${VERSION}" \
  -srcfolder "${STAGE_DIR}" \
  -ov \
  -format UDZO \
  "${DMG_PATH}" >/dev/null

echo "Created ${DMG_PATH}"
