# System Indicator Icons — Custom macOS-Style Tray Replacements

**Depends on:** 005 (System Tray Hosting), 018 (Dark/Light Theming)
**Blocks:** None directly

## Overview

Replace the native Windows system tray bitmap icons (volume, battery, Wi-Fi, Bluetooth) with custom WPF vector controls that match macOS Big Sur–Sequoia design language. Each indicator gets:

1. A **service** in `Harbor.Core` that reads system state via native APIs
2. **Icon geometries** (monochrome `PathGeometry` in `SystemIndicatorIcons.xaml`)
3. A **flyout** window following the CalendarFlyout pattern
4. **Tray filtering** to hide the native Windows version of the same icon
5. Integration into `TopMenuBar` and `App.xaml.cs` lifecycle

The right side of the menu bar reads (left to right): `[Tray Icons] [Bluetooth] [Wi-Fi] [Battery] [Volume] [Chevron] [Clock]`

---

## Phase 1: Volume (COMPLETED)

Phase 1 established all reusable patterns. See the implemented code for reference:

### Files Created
| File | Purpose |
|------|---------|
| `src/Harbor.Core/Services/VolumeService.cs` | NAudio `MMDeviceEnumerator` + `AudioEndpointVolume` for volume/mute/device tracking |
| `src/Harbor.Core/Services/TrayIconFilterService.cs` | `ICollectionView` filter over `NotificationArea.TrayIcons` |
| `src/Harbor.Shell/Controls/SystemIndicatorIcon.xaml(.cs)` | Reusable 18x18 UserControl with `IconData` dependency property |
| `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml` | `PathGeometry` resources for all icon states |
| `src/Harbor.Shell/Flyouts/VolumeFlyout.xaml(.cs)` | Floating flyout with slider + device list |

### Files Modified
| File | Change |
|------|--------|
| `Harbor.Core.csproj` | Added `NAudio` 2.2.1 |
| `DarkTheme.xaml` / `LightTheme.xaml` | Added flyout brushes (`FlyoutSliderTrackBackground`, `FlyoutSliderFillBrush`, `FlyoutSecondaryText`, `FlyoutItemHoverBackground`, `FlyoutCheckmarkBrush`) |
| `TopMenuBar.xaml` | Added `<controls:SystemIndicatorIcon x:Name="VolumeIcon">` in indicator StackPanel |
| `TopMenuBar.xaml.cs` | `ConnectVolumeService()`, icon state updates, flyout toggle |
| `App.xaml.cs` | `VolumeService` + `TrayIconFilterService` lifecycle |

### Patterns Established (reuse in all subsequent phases)

**Service pattern:**
- Implements `IDisposable`
- Exposes state properties + `EventHandler<T>` events for changes
- Constructor initializes, subscribes to OS callbacks
- Thread-safe with `lock`

**TopMenuBar integration pattern:**
- `Connect<X>Service(<X>Service)` method on TopMenuBar
- Subscribe to service events → `Dispatcher.Invoke(() => UpdateIcon(...))`
- Icon click → toggle flyout (same as `Clock_Click` / CalendarFlyout)
- Cleanup in `OnClosing`

**App.xaml.cs lifecycle pattern:**
- Create service after `_shellSettingsService`, before `_menuBar.Initialize()`
- Pass to `_menuBar.Connect<X>Service()`
- Dispose in `OnExit` in reverse order (before `_recycleBinService`)

**TrayIconFilterService pattern:**
- Add process names to `FilteredProcessNames` HashSet
- Add title substrings to a new array (like `VolumeSubstrings`)
- The filter is already wired to `TrayIconsControl.ItemsSource`

**Flyout pattern:**
- `Window` with `WindowStyle="None"`, `AllowsTransparency="True"`, `ShowInTaskbar="False"`, `Topmost="True"`
- Outer `Border`: `CornerRadius="16"`, `Background="{DynamicResource FlyoutBackground}"`, `DropShadowEffect`
- `OnDeactivated` → `Close()` (dismiss on click-outside)
- `OnClosed` → unsubscribe from service events
- Positioned 4px below the indicator icon's screen coordinates

---

## Phase 2: Battery

### 2.1 BatteryService

**New file:** `src/Harbor.Core/Services/BatteryService.cs`

No new NuGet packages needed — use `System.Windows.Forms.SystemInformation.PowerStatus` (already available since `Harbor.Shell` has `UseWindowsForms=true`) or the Win32 `GetSystemPowerStatus` API via CsWin32.

**Recommended approach:** Use CsWin32's `GetSystemPowerStatus` (add to `NativeMethods.txt`) since the service lives in `Harbor.Core` which doesn't reference WinForms. Poll every 60 seconds (battery state changes slowly; no efficient push callback exists).

**State model:**
```
ChargePercent: int (0-100)
IsCharging: bool
PowerSource: enum { Battery, AC, Unknown }
IconState: enum { Charging, Full, High, Medium, Low, Critical }
EstimatedMinutesRemaining: int? (null if unknown/charging)
```

**Thresholds:**
- Critical: ≤ 10%
- Low: 11–20%
- Medium: 21–50%
- High: 51–79%
- Full: 80–100%
- Charging: any % while plugged in (overrides the above)

**Events:** `BatteryChanged(BatteryChangedEventArgs)`

**Desktop detection:** If `GetSystemPowerStatus` reports `BatteryFlag = 128` (no battery), the service sets a `HasBattery = false` flag. TopMenuBar should hide the battery icon entirely when `HasBattery` is false.

**CsWin32:** Add `GetSystemPowerStatus` to `src/Harbor.Core/NativeMethods.txt`.

### 2.2 Battery Icon Geometries

**Modified file:** `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml`

Add `PathGeometry` resources (16x16 viewport, monochrome):

| Key | Description |
|-----|-------------|
| `BatteryFullIcon` | Full battery outline, filled body |
| `BatteryHighIcon` | Battery outline, ~75% filled |
| `BatteryMediumIcon` | Battery outline, ~50% filled |
| `BatteryLowIcon` | Battery outline, ~25% filled |
| `BatteryCriticalIcon` | Battery outline, thin sliver (≤10%) |
| `BatteryChargingIcon` | Battery outline + small lightning bolt overlay |

macOS battery icon anatomy: horizontal rounded rectangle (body), small nub on the right (positive terminal), fill level inside body. Lightning bolt centered when charging.

### 2.3 Battery Flyout

**New file:** `src/Harbor.Shell/Flyouts/BatteryFlyout.xaml(.cs)`

Follows VolumeFlyout pattern exactly.

**Flyout content (per macOS spec):**
- **Header:** "Battery" (SemiBold, 14px)
- **Percentage display:** Large text showing `ChargePercent%`
- **Status line:** "Charging", "On Battery Power", or "Fully Charged" in `FlyoutSecondaryText`
- **Time remaining:** "About X hours remaining" or "About X minutes remaining" (only shown when on battery and estimate available)
- **Separator**
- **Footer:** "Battery Settings..." → `Process.Start("ms-settings:batterysaver")`

### 2.4 Tray Filter Update

**Modified file:** `src/Harbor.Core/Services/TrayIconFilterService.cs`

Add to `FilteredProcessNames`:
- (Battery icons on Windows are typically rendered by `explorer.exe` or `ShellExperienceHost.exe` — these host multiple indicators, so we can't filter by process name alone)

Add battery title substrings:
```csharp
private static readonly string[] BatterySubstrings =
{
    "Battery",
    "battery",
    "Power",
    "Charging",
};
```

Update `FilterIcon` to also check battery substrings.

### 2.5 TopMenuBar + App.xaml.cs Integration

**Modified files:** `TopMenuBar.xaml`, `TopMenuBar.xaml.cs`, `App.xaml.cs`

XAML: Add `<controls:SystemIndicatorIcon x:Name="BatteryIcon" />` to the indicator StackPanel (after VolumeIcon, so it appears to its left visually — DockPanel.Dock="Right" means items added later appear further left).

Code-behind: Add `ConnectBatteryService(BatteryService)` following the volume pattern. Handle `HasBattery == false` → `BatteryIcon.Visibility = Collapsed`.

App.xaml.cs: Create `BatteryService` alongside `VolumeService`, pass to `ConnectBatteryService()`, dispose in `OnExit`.

### 2.6 Files Changed Summary

| Action | File |
|--------|------|
| **New** | `src/Harbor.Core/Services/BatteryService.cs` |
| **New** | `src/Harbor.Shell/Flyouts/BatteryFlyout.xaml` + `.xaml.cs` |
| Modify | `src/Harbor.Core/NativeMethods.txt` (add `GetSystemPowerStatus`) |
| Modify | `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml` (add battery geometries) |
| Modify | `src/Harbor.Core/Services/TrayIconFilterService.cs` (add battery filter) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml` (add BatteryIcon) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml.cs` (ConnectBatteryService, icon updates) |
| Modify | `src/Harbor.Shell/App.xaml.cs` (BatteryService lifecycle) |

---

## Phase 3: Wi-Fi / Network

### 3.1 NetworkService

**New file:** `src/Harbor.Core/Services/NetworkService.cs`

Use `System.Net.NetworkInformation.NetworkChange` for connectivity events and `NetworkInterface.GetAllNetworkInterfaces()` for enumeration. For Wi-Fi signal strength, use the Windows Native WiFi API (`WlanGetAvailableNetworkList`) via CsWin32 or direct P/Invoke.

**Simpler alternative (recommended for Phase 3):** Use `NetworkInterface` + `Wlan` managed wrappers. For signal strength, call `WlanQueryInterface` with `wlan_intf_opcode_current_connection` to get the current RSSI/signal quality. Add `WlanOpenHandle`, `WlanCloseHandle`, `WlanEnumInterfaces`, `WlanQueryInterface`, `WlanGetAvailableNetworkList`, `WlanFreeMemory` to `NativeMethods.txt` (or P/Invoke manually since CsWin32 may not cover wlanapi cleanly).

**State model:**
```
IsConnected: bool
ConnectionType: enum { None, WiFi, Ethernet, Cellular, Unknown }
WiFiNetworkName: string? (SSID, null if not Wi-Fi)
WiFiSignalStrength: enum { None, Weak, Fair, Good, Excellent }
IconState: enum { Disconnected, Ethernet, WiFiNone, WiFiWeak, WiFiFair, WiFiGood, WiFiExcellent }
AvailableNetworks: IReadOnlyList<WiFiNetwork> (for flyout)
```

**Wi-Fi signal thresholds (signal quality 0–100):**
- None: 0 (disconnected)
- Weak: 1–25
- Fair: 26–50
- Good: 51–75
- Excellent: 76–100

**Events:** `NetworkChanged(NetworkChangedEventArgs)`, `AvailableNetworksChanged`

**Polling:** Network state changes come via `NetworkChange.NetworkAvailabilityChanged` and `NetworkChange.NetworkAddressChanged` (push, no polling needed). Wi-Fi signal strength should be polled every 10 seconds when connected to Wi-Fi (signal fluctuates).

### 3.2 Wi-Fi Icon Geometries

**Modified file:** `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml`

macOS Wi-Fi icon: sector/fan shape with 3 concentric arcs radiating from a bottom-center point. Fill level indicates signal strength.

| Key | Description |
|-----|-------------|
| `WiFiExcellentIcon` | Full fan (all 3 arcs + dot) |
| `WiFiGoodIcon` | 2 arcs + dot |
| `WiFiFairIcon` | 1 arc + dot |
| `WiFiWeakIcon` | Dot only (smallest signal) |
| `WiFiDisconnectedIcon` | Full fan outline + diagonal slash |
| `EthernetIcon` | Diamond/globe shape (macOS uses `<>` shape for wired) |

### 3.3 Wi-Fi/Network Flyout

**New file:** `src/Harbor.Shell/Flyouts/NetworkFlyout.xaml(.cs)`

**Flyout content (per macOS spec):**
- **Header:** "Wi-Fi" (SemiBold, 14px)
- **Toggle row:** "Wi-Fi" label + toggle switch (on/off). When off, the icon shows `WiFiDisconnectedIcon` and the network list is hidden.
- **Current network:** Highlighted row with checkmark showing connected SSID + signal bars icon
- **Separator** with "Other Networks" subheader
- **Network list:** `ItemsControl` bound to `AvailableNetworks`. Each row: signal icon + SSID + lock icon (if secured). Click triggers connection (open Windows Wi-Fi settings as a fallback since programmatic WPA connection requires credentials UI).
- **Separator**
- **Footer:** "Network Settings..." → `Process.Start("ms-settings:network-wifi")`

**Ethernet fallback:** If `ConnectionType == Ethernet`, show "Ethernet" with the wired icon and no network list. The flyout simplifies to: header + "Connected via Ethernet" + "Network Settings..." link.

### 3.4 Tray Filter Update

**Modified file:** `src/Harbor.Core/Services/TrayIconFilterService.cs`

Add network title substrings:
```csharp
private static readonly string[] NetworkSubstrings =
{
    "Network",
    "Wi-Fi",
    "WiFi",
    "Ethernet",
    "Internet",
};
```

### 3.5 TopMenuBar + App.xaml.cs Integration

Same pattern as Volume/Battery. Add `WiFiIcon` to the indicator StackPanel (between BatteryIcon and tray icons). `ConnectNetworkService(NetworkService)`.

### 3.6 Files Changed Summary

| Action | File |
|--------|------|
| **New** | `src/Harbor.Core/Services/NetworkService.cs` |
| **New** | `src/Harbor.Shell/Flyouts/NetworkFlyout.xaml` + `.xaml.cs` |
| Modify | `src/Harbor.Core/NativeMethods.txt` (add Wlan APIs if using CsWin32) |
| Modify | `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml` (add Wi-Fi/Ethernet geometries) |
| Modify | `src/Harbor.Core/Services/TrayIconFilterService.cs` (add network filter) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml` (add WiFiIcon) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml.cs` (ConnectNetworkService, icon updates) |
| Modify | `src/Harbor.Shell/App.xaml.cs` (NetworkService lifecycle) |

---

## Phase 4: Bluetooth

### 4.1 BluetoothService

**New file:** `src/Harbor.Core/Services/BluetoothService.cs`

Use `Windows.Devices.Bluetooth` (WinRT) and `Windows.Devices.Radios` for Bluetooth adapter state. Requires adding the `Microsoft.Windows.SDK.Contracts` NuGet or using WinRT projection (`<TargetFramework>net10.0-windows10.0.22621</TargetFramework>` already enables this).

**Simpler approach:** Use `Windows.Devices.Radios.Radio` to detect Bluetooth adapter state (On/Off/Disabled) and `Windows.Devices.Enumeration.DeviceInformation.FindAllAsync` with a Bluetooth selector to enumerate paired devices.

**State model:**
```
IsAvailable: bool (hardware exists)
IsEnabled: bool (radio is on)
ConnectedDeviceCount: int
ConnectedDevices: IReadOnlyList<BluetoothDevice>
IconState: enum { Off, On, Connected }
```

**BluetoothDevice model:**
```
Name: string
Category: enum { Audio, Keyboard, Mouse, Phone, Other }
IsConnected: bool
BatteryPercent: int? (null if not reported)
```

**Events:** `BluetoothChanged(BluetoothChangedEventArgs)`, `DevicesChanged`

**Push notifications:** Use `Radio.StateChanged` event for adapter on/off. Use `DeviceWatcher` from `Windows.Devices.Enumeration` to monitor device connect/disconnect in real-time (no polling needed).

### 4.2 Bluetooth Icon Geometries

**Modified file:** `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml`

macOS Bluetooth icon: the classic Bluetooth runic "B" shape.

| Key | Description |
|-----|-------------|
| `BluetoothOnIcon` | Bluetooth rune shape (filled) |
| `BluetoothConnectedIcon` | Bluetooth rune + small connected dots on either side |
| `BluetoothOffIcon` | Bluetooth rune + diagonal slash |

### 4.3 Bluetooth Flyout

**New file:** `src/Harbor.Shell/Flyouts/BluetoothFlyout.xaml(.cs)`

**Flyout content (per macOS spec):**
- **Header:** "Bluetooth" (SemiBold, 14px)
- **Toggle row:** "Bluetooth" label + toggle switch (on/off). Toggle calls `Radio.SetStateAsync(RadioState.On/Off)`.
- **Separator** with "Devices" subheader (only shown when Bluetooth is on)
- **Connected devices list:** Each row: device name + category icon + battery % (if available) + "Connected" label. Click could open device settings.
- **Paired but disconnected devices:** Listed below connected devices in `FlyoutSecondaryText` color.
- **Separator**
- **Footer:** "Bluetooth Settings..." → `Process.Start("ms-settings:bluetooth")`

### 4.4 Tray Filter Update

**Modified file:** `src/Harbor.Core/Services/TrayIconFilterService.cs`

Add Bluetooth title substrings:
```csharp
private static readonly string[] BluetoothSubstrings =
{
    "Bluetooth",
};
```

### 4.5 TopMenuBar + App.xaml.cs Integration

Same pattern. Add `BluetoothIcon` to indicator StackPanel (leftmost indicator, so it appears closest to tray icons). `ConnectBluetoothService(BluetoothService)`.

Handle `IsAvailable == false` → `BluetoothIcon.Visibility = Collapsed` (machines without Bluetooth hardware).

### 4.6 Files Changed Summary

| Action | File |
|--------|------|
| **New** | `src/Harbor.Core/Services/BluetoothService.cs` |
| **New** | `src/Harbor.Shell/Flyouts/BluetoothFlyout.xaml` + `.xaml.cs` |
| Modify | `src/Harbor.Shell/Resources/SystemIndicatorIcons.xaml` (add Bluetooth geometries) |
| Modify | `src/Harbor.Core/Services/TrayIconFilterService.cs` (add Bluetooth filter) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml` (add BluetoothIcon) |
| Modify | `src/Harbor.Shell/TopMenuBar.xaml.cs` (ConnectBluetoothService, icon updates) |
| Modify | `src/Harbor.Shell/App.xaml.cs` (BluetoothService lifecycle) |

---

## Final Menu Bar Layout (All Phases Complete)

```
[Apple] [AppName] [File] [Edit] ...     [TrayIcons] [BT] [WiFi] [Battery] [Volume] [›] [Mon Feb 24  3:45 PM]
                                          ← filtered →  ← custom vector indicators →
```

## Verification (Per Phase)

1. `dotnet build Harbor.slnx` — compiles clean
2. `dotnet test Harbor.slnx` — existing tests pass
3. `dotnet run --project src/Harbor.Shell` — visual verification:
   - Icon appears in menu bar at correct position
   - Icon state reflects current system state
   - Real-time updates (change volume, plug/unplug charger, connect/disconnect WiFi/BT)
   - Clicking icon opens flyout with correct content
   - Flyout controls work (slider, toggle, device list)
   - Flyout dismisses on click-outside
   - Native tray icon for that indicator is filtered out
   - Light/dark theme switching updates colors
   - Hardware absence hides icon (no battery → no battery icon, no BT adapter → no BT icon)

## Implementation Order

| Phase | Indicator | Complexity | Key API |
|-------|-----------|-----------|---------|
| 1 | Volume | Low | NAudio `MMDeviceEnumerator` (DONE) |
| 2 | Battery | Low | `GetSystemPowerStatus` (Win32, no NuGet) |
| 3 | Wi-Fi/Network | Medium | `NetworkChange` + Native WiFi API |
| 4 | Bluetooth | Medium | WinRT `Windows.Devices.Radios` + `DeviceWatcher` |

Battery is next because it's the simplest remaining (single Win32 call, polling, no complex flyout interaction). Wi-Fi and Bluetooth involve more complex enumeration and real-time device tracking.
