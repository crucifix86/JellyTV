#!/bin/bash
# Download and bundle VLC with all plugins including X11

set -e

VLC_DIR="runtimes/linux-x64/native"
TEMP_DIR=$(mktemp -d)

echo "Downloading VLC packages..."
cd "$TEMP_DIR"

# Download VLC and required plugins for Debian
apt-get download \
    vlc-plugin-base \
    vlc-plugin-video-output \
    libvlc5 \
    libvlccore9

echo "Extracting VLC libraries and plugins..."
for deb in *.deb; do
    dpkg-deb -x "$deb" extracted/
done

# Create directory structure
mkdir -p "$(dirname "$0")/$VLC_DIR/vlc/plugins"

# Copy VLC libraries
cp -r extracted/usr/lib/x86_64-linux-gnu/libvlc*.so* "$(dirname "$0")/$VLC_DIR/" 2>/dev/null || true

# Copy all VLC plugins
cp -r extracted/usr/lib/x86_64-linux-gnu/vlc/plugins/* "$(dirname "$0")/$VLC_DIR/vlc/plugins/" 2>/dev/null || true

echo "Cleaning up..."
cd -
rm -rf "$TEMP_DIR"

echo "VLC with full plugins bundled successfully!"
ls -la "$(dirname "$0")/$VLC_DIR/vlc/plugins/video_output/"
