# AutoLock

AutoLock is a Windows proximity-lock app for Bluetooth devices. After binding a nearby Bluetooth LE signal, it monitors the signal and locks Windows when the device has been missing for a configured grace period.

## Important Limits

Many modern Bluetooth devices, including Apple Watch, iPhone, and Android phones, use Bluetooth privacy features, so a stable public MAC address is not always available. A practical Windows implementation should treat the Bluetooth address as one signal, and bind using a combination of:

- Current Bluetooth LE address, when visible.
- Advertised local name, when visible.
- Manufacturer data presence.
- RSSI history and disappearance timing.
- Optional manual confirmation during enrollment.

For strong identity, each device platform would need to expose an authenticated companion protocol to Windows. This project therefore implements a pragmatic proximity lock, not cryptographic device authentication.

## Windows App

The app is [`src/AutoLock.WinUI`](src/AutoLock.WinUI). It uses WinUI 3 for the Windows 11-style interface and [`src/AutoLock.Core`](src/AutoLock.Core) for Bluetooth scanning, binding, monitoring, startup registration, and workstation locking.

It scans Bluetooth LE advertisements, lets you bind a visible candidate, then locks the workstation when the signal disappears for long enough.

Build with the .NET SDK on Windows:

```shell
dotnet build .\src\AutoLock.WinUI\AutoLock.WinUI.csproj
```

GitHub Actions builds the WinUI project on `push` and `pull_request`. Non-PR runs upload the portable build and a test-signed MSIX as separate x64 artifacts. The MSIX artifact includes its public test certificate and elevated installation scripts, but never the private key. Artifact downloads are already ZIP files, so neither contains a second nested portable ZIP. A manual release workflow can generate the portable artifact, MSIX artifact, or both from the Actions tab.

Run the UI:

```shell
dotnet run --project .\src\AutoLock.WinUI\AutoLock.WinUI.csproj
```

Package a release build:

```powershell
.\scripts\publish.ps1 -Mode Folder -RuntimeIdentifier win-x64 -Version 1.0.0 -Clean
```

For MSIX signing, test certificates, and release validation, see [`docs/release.md`](docs/release.md).

Use `Test mode, do not lock` when you want monitoring to report that Windows would lock without actually locking. The setting is persisted with the rest of the app settings.

Use `Test Lock` to immediately verify Windows locking. If test mode is enabled, AutoLock reports the dry-run result instead of locking.

`Min RSSI` controls the weakest accepted signal in dBm. The default is `-90`; use a stricter value such as `-80` if distant signals still count as nearby, or a looser value such as `-100` if the bound device is ignored too easily.

The UI includes English and Simplified Chinese language packs. AutoLock chooses Chinese automatically when the Windows UI culture starts with `zh`; the language option is available from the settings page.

The History page records binding, scan, monitor, lock, unlock, and background-mode events. The About page shows the version and local diagnostic paths, including configuration, history, and crash-log files.

Use the 15-minute pause button for short trusted periods. In Settings, enable `Do not lock on external power` / `接入电源时不锁` if AutoLock should skip automatic locking while the PC is plugged in. You can also save multiple trusted Wi-Fi SSIDs so AutoLock skips automatic locking while connected to any listed network.

The app icon is stored in [`src/AutoLock.WinUI/Assets/AppIcon.ico`](src/AutoLock.WinUI/Assets/AppIcon.ico), with [`src/AutoLock.WinUI/Assets/AppIcon.svg`](src/AutoLock.WinUI/Assets/AppIcon.svg) as the editable source. The mark combines a lock, wearable-style device silhouette, and Bluetooth-like signal arcs.

Enable `Start AutoLock when Windows signs in` / `登录 Windows 时自动启动 AutoLock` to register AutoLock under the current user's startup apps. The app writes to:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

This does not require administrator permissions and affects only the current Windows user.

Enable `Run in background when minimized or closed` / `最小化或关闭窗口时在后台运行` to keep AutoLock alive in the Windows notification area. Minimize or close the window to hide it to the tray, double-click the tray icon to reopen it, or use the tray menu to exit. When both startup and background mode are enabled, the startup command includes `--background` so AutoLock starts directly in the tray after sign-in.

## IRK Configuration

IRK means Identity Resolving Key. For ordinary Bluetooth devices that advertise a stable enough identifier, binding the scanned device may be sufficient. Apple Watch and some privacy-focused devices rotate private Bluetooth addresses, so binding only the address shown during one scan can stop working later. With the IRK, AutoLock can identify whether a rotating private address belongs to the bound Apple Watch.

Recommended acquisition method:

1. On Apple Watch, open Settings -> General -> About -> Bluetooth and write down the Bluetooth address.
2. Use a Mac signed into the same iCloud account as the Apple Watch.
3. Open Keychain Access, search for Bluetooth records, and find the entry whose account matches the watch Bluetooth address.
4. Show/copy the stored value and decode it into a 32-character hex IRK.
5. Paste that IRK into AutoLock before binding the device.

Windows alone cannot directly extract the Apple Watch IRK. For Apple Watch, avoid relying on the scanned BLE address alone unless you are only doing a quick test.

Reference guides:

- [ZuUnlock Apple Watch configuration](https://zu.3gxk.net/docs/guide/apple-watch.html)
- [ZuUnlock Apple Watch IRK acquisition](https://zu.3gxk.net/docs/guide/irk-apple-watch.html)

## Product Design

### Enrollment

1. User opens AutoLock and chooses "Bind Bluetooth Device".
2. The app scans BLE advertisements for 15-30 seconds.
3. Candidates are ranked by manufacturer data, local name, RSSI, and repeat sightings.
4. User selects a candidate while the device is physically nearby.
5. The app stores a local binding profile in the user's AppData directory.

### Monitoring

1. A background process keeps a BLE advertisement watcher active.
2. Each matching sighting updates `lastSeen`, `lastRssi`, and confidence.
3. If the device is not seen for `MissingSeconds`, the app enters a warning state.
4. If still missing after the debounce window, the app calls `LockWorkStation`.
5. After locking, the app cools down to avoid repeated lock calls.

### False Positive Controls

- Default missing threshold should be 30 seconds, not instant.
- Ignore short RSSI drops.
- Require repeated sightings during enrollment.
- Use trusted scenarios such as external power and trusted Wi-Fi to suppress locking when appropriate.
- Show a tray indicator for current signal state.

### Formal Windows App

A production version should be a per-user tray app, not a Windows service, because BLE access and workstation lock are both naturally tied to the interactive user session.

Suggested stack:

- C# / .NET 9 or 8
- WinUI 3 tray UI
- `Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher`
- P/Invoke `user32.dll LockWorkStation`
- JSON config under `%LOCALAPPDATA%\AutoLock\config.json`
- Optional startup registration via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## Security Model

AutoLock should be described as convenience security. It reduces the chance of leaving a Windows session open when the user walks away, but it should not replace password, PIN, Windows Hello, or organization-managed device policy.
