# JellyTV

A TV-optimized interface for Jellyfin media server with gamepad support.

## Features

### Completed
- Auto-login with saved credentials
- Home menu with media browsing
- Settings menu with multiple sections:
  - Audio Output selection (PulseAudio sink switching)
  - Video Output selection (xrandr-based display configuration)
  - Bluetooth device management (scan, connect, disconnect, remove)
  - Account management (logout)
- On-screen keyboard for text input
- Gamepad input support
- Media playback with LibVLC

### In Progress
- Bluetooth device pairing functionality
- Button mapping configuration

## Current Known Issues

### Bluetooth Pairing
The Bluetooth pairing functionality is currently not working due to agent registration issues. The error occurs because:

1. `bluetoothctl` requires a Bluetooth agent to handle pairing authentication
2. Xbox Wireless Controllers use SSP (Secure Simple Pairing) which requires agent confirmation
3. Current implementation attempts to register agent but bluetoothd reports "No agent available for request type 2"

**Status**: BluetoothService.cs has been updated with agent registration logic (agent on, default-agent) but needs testing after system restart.

**Logs show**:
```
src/device.c:new_auth() No agent available for request type 2
device_confirm_passkey: Operation not permitted
```

**Next Steps**:
- Test pairing after system restart
- May need to implement D-Bus based Bluetooth management instead of bluetoothctl command-line approach
- Consider using bluez D-Bus API directly for more reliable pairing

## Services

### BluetoothService
- Manages Bluetooth operations via bluetoothctl
- Provides device scanning, pairing, connection, and removal
- **Known Issue**: Pairing requires proper agent registration

### AudioService
- Lists available PulseAudio sinks
- Sets default audio output device
- Automatically moves playing streams to new sink

### VideoService
- Queries connected displays via xrandr
- Switches video output and resolution
- Disables non-primary outputs

### GamepadInputService
- Reads input from /dev/input/js0
- Maps gamepad buttons to UI actions
- Navigation support for menus

### MediaPlayerService
- LibVLC-based media playback
- Integrated with Jellyfin streaming

## Dependencies

- .NET 8.0
- Avalonia UI
- LibVLC
- PulseAudio (pactl)
- xrandr
- bluetoothctl (bluez)

## Build and Run

```bash
dotnet build
DISPLAY=:0 dotnet run
```

## Git Repository

Repository: https://github.com/crucifix86/JellyTV
