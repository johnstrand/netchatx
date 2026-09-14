#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
RID="${2:-osx-arm64}"
PUBLISH_DIR="${3:-publish/${RID}}"
OUTPUT_DIR="${4:-dist}"

mkdir -p "${OUTPUT_DIR}"
OUTPUT_DIR="$(cd "${OUTPUT_DIR}" && pwd)"

APP_NAME="NetChatx.app"
BUNDLE_DIR="bundle/${APP_NAME}"
rm -rf bundle
mkdir -p "${BUNDLE_DIR}/Contents/MacOS"
mkdir -p "${BUNDLE_DIR}/Contents/Resources"

echo "Populating macOS app bundle..."
cp -R "${PUBLISH_DIR}"/* "${BUNDLE_DIR}/Contents/MacOS/"
chmod +x "${BUNDLE_DIR}/Contents/MacOS/NetChatx.Gui"

echo "APPL????" > "${BUNDLE_DIR}/Contents/PkgInfo"

sed "s/__VERSION__/${VERSION}/g" packaging/macos/Info.plist.template > "${BUNDLE_DIR}/Contents/Info.plist"

# Generate .icns icon
if [ -f "src/NetChatx.Gui/Assets/netchatx-logo.png" ]; then
    echo "Generating macOS .icns icon..."
    rm -rf icon.iconset
    mkdir -p icon.iconset
    sips -z 16 16     src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_16x16.png
    sips -z 32 32     src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_16x16@2x.png
    sips -z 32 32     src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_32x32.png
    sips -z 64 64     src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_32x32@2x.png
    sips -z 128 128   src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_128x128.png
    sips -z 256 256   src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_128x128@2x.png
    sips -z 256 256   src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_256x256.png
    sips -z 512 512   src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_256x256@2x.png
    sips -z 512 512   src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_512x512.png
    sips -z 1024 1024 src/NetChatx.Gui/Assets/netchatx-logo.png --out icon.iconset/icon_512x512@2x.png
    iconutil -c icns icon.iconset -o "${BUNDLE_DIR}/Contents/Resources/netchatx.icns"
    rm -rf icon.iconset
fi

# Ad-hoc code sign bundle
if command -v codesign &> /dev/null; then
    echo "Signing macOS app bundle..."
    codesign --force --deep --sign - "${BUNDLE_DIR}"
fi

# Create ZIP of .app bundle
ZIP_NAME="NetChatx-v${VERSION}-${RID}.zip"
echo "Creating ZIP archive: ${OUTPUT_DIR}/${ZIP_NAME}..."
(cd bundle && zip -r -y "${OUTPUT_DIR}/${ZIP_NAME}" "${APP_NAME}")

# Create DMG installer
DMG_NAME="NetChatx-v${VERSION}-${RID}.dmg"
echo "Creating DMG installer: ${OUTPUT_DIR}/${DMG_NAME}..."
DMG_STAGING="dmg-staging"
rm -rf "${DMG_STAGING}"
mkdir -p "${DMG_STAGING}"
cp -R "${BUNDLE_DIR}" "${DMG_STAGING}/"
ln -s /Applications "${DMG_STAGING}/Applications"

hdiutil create -volname "NetChatx" -srcfolder "${DMG_STAGING}" -ov -format UDZO "${OUTPUT_DIR}/${DMG_NAME}"
rm -rf "${DMG_STAGING}" bundle

echo "macOS packaging completed: ${OUTPUT_DIR}/${ZIP_NAME}, ${OUTPUT_DIR}/${DMG_NAME}"
