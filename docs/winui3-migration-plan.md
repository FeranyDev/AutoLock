# WinUI 3 Status

AutoLock has been consolidated to the WinUI 3 implementation.

## Active Projects

```text
src/
  AutoLock.Core/      Shared configuration, BLE scanning, IRK matching, monitoring, startup, and lock logic.
  AutoLock.WinUI/     WinUI 3 desktop app and Windows 11-style UI.
```

## Removed Projects

The earlier UI prototypes have been removed from the active codebase. UI work should happen in `AutoLock.WinUI`; shared non-UI behavior should stay in `AutoLock.Core`.

## Current Scope

- Bind a nearby Bluetooth LE device.
- Unbind the current Bluetooth device.
- Optionally use IRK matching for Apple Watch-style private address rotation.
- Monitor RSSI and missing-signal timeout.
- Lock Windows through `LockWorkStation`.
- Stop detection after timeout lock and reset after unlock.
- Support startup registration, background tray mode, app icon, and Chinese/English UI.
- Record a lightweight local history of binding, scanning, monitoring, and lock events.
- Persist scan duration and dry-run settings.
- Support a 15-minute manual pause, an external-power trust option, and a trusted Wi-Fi SSID that suppress automatic locking.
- Show an About/Diagnostics page with version and local data paths.
