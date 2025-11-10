#!/bin/bash
# JellyTV Development Launcher - Sets up environment for bundled VLC libraries and runs with dotnet

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Set LD_LIBRARY_PATH to include bundled VLC libraries
export LD_LIBRARY_PATH="$SCRIPT_DIR/bin/Debug/net8.0/runtimes/linux-x64/native:$LD_LIBRARY_PATH"
export VLC_PLUGIN_PATH="$SCRIPT_DIR/bin/Debug/net8.0/runtimes/linux-x64/native/vlc/plugins"

echo "Starting JellyTV (development mode) with bundled VLC libraries..."
echo "LD_LIBRARY_PATH=$LD_LIBRARY_PATH"
echo "VLC_PLUGIN_PATH=$VLC_PLUGIN_PATH"

# Run with dotnet run
cd "$SCRIPT_DIR"
DISPLAY=:0 dotnet run --project JellyTV.csproj "$@"
