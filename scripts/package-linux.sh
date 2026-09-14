#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
PUBLISH_DIR="${2:-publish/linux-x64}"
OUTPUT_DIR="${3:-dist}"

mkdir -p "${OUTPUT_DIR}"

TAR_NAME="NetChatx-v${VERSION}-linux-x64.tar.gz"
echo "Creating Linux tarball: ${OUTPUT_DIR}/${TAR_NAME}..."
tar -czvf "${OUTPUT_DIR}/${TAR_NAME}" -C "${PUBLISH_DIR}" .

echo "Creating Debian (.deb) package..."
DEB_DIR="deb-package"
rm -rf "${DEB_DIR}"
mkdir -p "${DEB_DIR}/DEBIAN"
mkdir -p "${DEB_DIR}/usr/lib/netchatx"
mkdir -p "${DEB_DIR}/usr/bin"
mkdir -p "${DEB_DIR}/usr/share/applications"
mkdir -p "${DEB_DIR}/usr/share/icons/hicolor/512x512/apps"

# Copy published files
cp -r "${PUBLISH_DIR}"/* "${DEB_DIR}/usr/lib/netchatx/"
chmod +x "${DEB_DIR}/usr/lib/netchatx/NetChatx.Gui"

# Create symlink
ln -sf /usr/lib/netchatx/NetChatx.Gui "${DEB_DIR}/usr/bin/netchatx"

# Copy desktop and icon files
cp packaging/linux/netchatx.desktop "${DEB_DIR}/usr/share/applications/"
cp src/NetChatx.Gui/Assets/netchatx-logo.png "${DEB_DIR}/usr/share/icons/hicolor/512x512/apps/netchatx.png"

# Generate control file
cat <<EOF > "${DEB_DIR}/DEBIAN/control"
Package: netchatx
Version: ${VERSION}
Section: net
Priority: optional
Architecture: amd64
Maintainer: NetChatx Contributors
Description: Modern cross-platform XMPP/Jabber client
 NetChatx is a fast, lightweight, and modern cross-platform desktop XMPP client built with .NET and Avalonia.
EOF

# Permissions
chmod -R 0755 "${DEB_DIR}"
chmod 0644 "${DEB_DIR}/DEBIAN/control"
chmod 0644 "${DEB_DIR}/usr/share/applications/netchatx.desktop"
chmod 0644 "${DEB_DIR}/usr/share/icons/hicolor/512x512/apps/netchatx.png"
chmod 0755 "${DEB_DIR}/usr/lib/netchatx/NetChatx.Gui"

DEB_NAME="NetChatx-v${VERSION}-linux-x64.deb"
dpkg-deb --build "${DEB_DIR}" "${OUTPUT_DIR}/${DEB_NAME}"
rm -rf "${DEB_DIR}"

echo "Linux packaging completed: ${OUTPUT_DIR}/${TAR_NAME}, ${OUTPUT_DIR}/${DEB_NAME}"
