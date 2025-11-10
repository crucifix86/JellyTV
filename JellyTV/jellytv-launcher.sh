#!/bin/bash
# JellyTV Launcher - Sets up environment for bundled VLC libraries

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Set LD_LIBRARY_PATH to include bundled VLC libraries
export LD_LIBRARY_PATH="$SCRIPT_DIR/runtimes/linux-x64/native:$LD_LIBRARY_PATH"
export VLC_PLUGIN_PATH="$SCRIPT_DIR/runtimes/linux-x64/native/vlc/plugins"

echo "Starting JellyTV with bundled VLC libraries..."
echo "LD_LIBRARY_PATH=$LD_LIBRARY_PATH"

# Run the actual application
exec "$SCRIPT_DIR/JellyTV" "$@"
