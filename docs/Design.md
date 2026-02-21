  
**HARBOR**

Shell Replacement for Windows 11

Architecture & Design Document — Overlay Model

**Version 2.0**

February 2026

*Status: Draft — For Engineering Review*

# **1\. Executive Summary**

Harbor is a custom Windows 11 shell replacement designed to provide a macOS-style desktop paradigm. The MVP replaces explorer.exe to render a persistent Top Menu bar, a dynamic Dock, and macOS-style window controls. Harbor achieves this by utilizing the Windows UI Automation (UIA) API to map application windows and deploying borderless, transparent WPF overlays that intercept clicks and draw custom controls without modifying target application memory or triggering Endpoint Detection and Response (EDR) alerts.

This document (v2.0) expands on the original design to address overlay synchronization, per-application edge cases, multi-monitor DPI scaling, title bar color detection, fullscreen handling, crash recovery, and testing strategy. These topics have been promoted from footnotes to first-class design concerns, as they represent the primary engineering risks for the project.

# **2\. Technical Stack**

| Component | Selection |
| :---- | :---- |
| **Language** | C\# (.NET 10 LTS\) |
| **Core Shell Library** | ManagedShell (cairoshell/ManagedShell) — Apache 2.0 licensed, community-maintained. Handles AppBar screen reservation, system tray interception, and task enumeration. |
| **UI Framework** | WPF (Windows Presentation Foundation) for shell chrome; Win2D/DirectComposition evaluated as fallback for overlay layer (see Section 7). |
| **Automation** | UIAutomationClient / UIAutomationTypes — the native Windows accessibility API used by screen readers to enumerate bounding boxes of windows and controls. |
| **DPI Awareness** | DPI\_AWARENESS\_CONTEXT\_PER\_MONITOR\_AWARE\_V2 declared in application manifest. |

## **2.1 Dependency Risk: ManagedShell**

ManagedShell is a community-maintained project originating from the Cairo Desktop Environment. It currently targets .NET 6/8 and supports running as an explorer.exe replacement or alongside it. Because Harbor's AppBar registration, tray hosting, and task enumeration all depend on this library, its maintenance status is a supply-chain risk.

**Mitigation:** Pin to a known-good release tag. Fork the repository under the Harbor organization. Assign one engineer to monitor upstream commits quarterly. If upstream goes dormant for 6+ months, promote the fork to primary and absorb maintenance.

# **3\. Core MVP Components**

## **3A. The Top Menu (System Bar)**

**Implementation:** A WPF Window registered via ManagedShell as a top-docked AppBar.

* Left Side (Global Menu): Reads the foreground window using GetForegroundWindow(), displaying the active application name. Future iterations may parse UIA menu structures to populate a functional global menu bar.

* Right Side (System Tray): Utilizes ManagedShell's TrayService to natively host standard Windows notification area icons (network, volume, background tasks).

## **3B. The Dock**

**Implementation:** A WPF Window registered as a bottom-docked AppBar.

* Behavior: Bound to ManagedShell's TaskService. Dynamically updates based on EVENT\_OBJECT\_CREATE and EVENT\_OBJECT\_DESTROY system events.

* Interaction: Extracts high-resolution icons from active executables via SHGetFileInfo. Falls back to icon extraction from the executable's resource table if SHGetFileInfo returns a generic icon.

# **4\. Emulating macOS Window Controls (The UIA Overlay Layer)**

This phase relies on treating the entire Windows OS like a massive DOM or widget tree. Every window, title bar, and button is a node. Harbor reads this tree using the UI Automation client API to position its custom UI on top of existing applications.

## **4A. Left-Aligned Window Action Buttons (Traffic Lights via Overlay)**

Instead of modifying the target application, Harbor draws a mask over it.

**UIA Tracking Pipeline**

**Step 1 — Event Subscription:** Harbor subscribes to SetWinEventHook for EVENT\_SYSTEM\_FOREGROUND and EVENT\_OBJECT\_LOCATIONCHANGE. When an application becomes active or moves, Harbor queries the UIA tree.

**Step 2 — Title Bar Discovery:** Harbor uses the UIA client API to walk the target window's automation element tree, locating the element with ControlType.TitleBar. It reads BoundingRectangle to get the exact (X, Y, Width, Height) of the native title bar. If UIA discovery fails (see Section 6A), Harbor falls back to NONCLIENT area heuristics.

**Step 3 — Overlay Spawn:** Harbor creates a borderless, transparent WPF Window with extended styles WS\_EX\_LAYERED | WS\_EX\_NOACTIVATE. This ensures clicking the overlay does not steal focus from the underlying application.

**Step 4 — Overlay Positioning:** The overlay is sized and positioned to match the native title bar's bounding rectangle. On the right side, Harbor draws a solid color block to obscure the native minimize/maximize/close buttons. On the left side, Harbor draws interactive macOS traffic-light controls.

**Step 5 — Click Routing:** When a user clicks Harbor's custom close button, Harbor programmatically sends an SC\_CLOSE message to the underlying window handle. Minimize sends SC\_MINIMIZE (or triggers the custom genie animation). Maximize sends SC\_MAXIMIZE / SC\_RESTORE.

## **4B. Custom Minimize Animation (Genie Effect)**

Because Harbor controls the click event on the overlay buttons, it controls the animation trigger.

**Prerequisites**

* Disable native Windows animations via SystemParametersInfo with SPI\_SETCLIENTAREAANIMATION (set to FALSE). Restore the original value on Harbor shutdown.

**Animation Sequence**

* **1\. Bitmap Capture:** Harbor captures a live bitmap of the target window using the DwmRegisterThumbnail API.

* **2\. Instant Hide:** Harbor sends ShowWindow(SW\_HIDE) to the native window to make it vanish instantly. The HWND is recorded in the Hidden Window Registry (see Section 10B).

* **3\. Animation Canvas:** Harbor spawns a fullscreen transparent overlay and animates the bitmap thumbnail along a bezier curve, shrinking it toward the precise coordinates of its Dock icon.

* **4\. Completion:** On animation end, Harbor sends the actual SW\_MINIMIZE command and removes the animation canvas.

# **5\. UI/UX Specification**

This section defines the visual design and interaction behavior for Harbor's three MVP components. All specs target macOS Sequoia's appearance as the reference. Measurements are given in device-independent pixels (DIPs) at 1× scale; Harbor's per-monitor DPI logic (Section 8A) applies the appropriate multiplier at runtime.

## **5A. Top Menu Bar (System Bar)**

**Dimensions & Material**

| Property | Value |
| :---- | :---- |
| **Height** | 24 DIP |
| **Background** | Dark mode: `#1E1E1E` at 80% opacity with 30px Gaussian blur (Acrylic). Light mode: `#F6F6F6` at 80% opacity with 30px Gaussian blur. |
| **Font** | `SF Pro Text` mapped to `Segoe UI Variable` on Windows, 13px, regular weight (400). |
| **Text color** | Dark mode: `#FFFFFF`. Light mode: `#000000`. |
| **Bottom border** | 1px solid `#3A3A3A` (dark) / `#D1D1D1` (light). |

**Left Side — Harbor Icon & App Name**

* Harbor icon: 16×16 DIP, 8px left padding.
* Active app name: bold weight (600), 8px left of icon.
* Menu items (File, Edit, View…): regular weight (400), 16px horizontal padding each.

**Right Side — System Tray & Clock**

* System tray icons: 18×18 DIP, 8px spacing between icons, 8px right margin.
* Clock: right-aligned, 12px right padding. Format: `h:mm AM/PM` (follows Windows regional setting).
* Control Center indicator: chevron glyph `›`, 10px left of clock.

**Interaction States**

| State | Appearance |
| :---- | :---- |
| **Default** | Text only, no background highlight. |
| **Hover** | Rounded rectangle highlight `#FFFFFF` at 10% opacity (dark) / `#000000` at 8% opacity (light), 4px corner radius, 120ms fade-in (`ease-out`). |
| **Pressed** | Highlight at 20% opacity, immediate (no transition). |
| **Disabled / inactive menu** | Text at 50% opacity. |

## **5B. Dock**

**Dimensions & Material**

| Property | Value |
| :---- | :---- |
| **Bar height** | 62 DIP (includes padding). |
| **Icon size** | 48×48 DIP (default). |
| **Padding** | 8px vertical, 4px horizontal between icons. |
| **Corner radius** | 16px (overall Dock container). |
| **Bottom margin** | 4px from screen edge. |
| **Background** | `#1E1E1E` at 50% opacity with 40px Gaussian blur (Acrylic). Light mode: `#F6F6F6` at 50% opacity with 40px blur. |
| **Border** | 1px solid `#FFFFFF` at 12% opacity (dark) / `#000000` at 8% opacity (light). |

**Separator**

* 1px wide vertical line, `#FFFFFF` at 20% opacity (dark) / `#000000` at 12% opacity (light), 24 DIP tall, centered vertically. Separates pinned apps from running (unpinned) apps.

**Icon States**

| State | Behavior |
| :---- | :---- |
| **Default** | 48×48 DIP icon, no transform. |
| **Hover** | Scale to 56×56 DIP (1.167×), 150ms `ease-out`. Tooltip appears after 500ms hover delay: app name in a rounded pill (`#1E1E1E` background, `#FFFFFF` text, 6px 12px padding, 8px corner radius), positioned 8px above the icon. |
| **Active (running)** | Small indicator dot below the icon: 4px diameter circle, `#FFFFFF` (dark) / `#000000` (light), centered, 2px below icon bottom edge. |
| **Launching** | Bounce animation: icon translates 12 DIP upward then returns to rest, repeated 3 times. Each bounce: 300ms total (150ms up `ease-out`, 150ms down `ease-in`). Total duration: 900ms. |
| **Pressed** | Scale to 44×44 DIP (0.917×), 80ms `ease-in`. Returns to 48×48 on release, 100ms `ease-out`. |

**Auto-Hide Behavior**

* Trigger zone: 2px tall invisible hit-test region at screen bottom edge.
* Reveal delay: 200ms after cursor enters trigger zone.
* Show animation: slide up from below screen edge, 250ms, `cubic-bezier(0.16, 1, 0.3, 1)`.
* Hide delay: 1000ms after cursor leaves the Dock area.
* Hide animation: slide down below screen edge, 200ms, `cubic-bezier(0.7, 0, 0.84, 0)`.

**Right-Click Context Menu**

* Items for pinned apps: "Open", "Remove from Dock", separator, "Options ▸" (submenu: "Keep in Dock", "Open at Login").
* Items for running apps: "Open", separator, "Options ▸" (submenu: "Keep in Dock", "Open at Login"), separator, "Quit".
* Menu style: matches system context menu styling with Acrylic background.

## **5C. Traffic Light Window Controls**

**Dimensions & Positioning**

| Property | Value |
| :---- | :---- |
| **Button diameter** | 12 DIP |
| **Spacing between buttons** | 8 DIP (center to center: 20 DIP) |
| **Padding from left edge** | 8 DIP from overlay left edge to first button center |
| **Vertical centering** | Centered vertically within the title bar overlay |

**Button Colors**

| Button | Default | Hover glyph | Pressed | Inactive window |
| :---- | :---- | :---- | :---- | :---- |
| **Close** | `#FF5F57` | `×` (cross) | `#E0443E` | `#CDCDCD` |
| **Minimize** | `#FEBC2E` | `−` (minus) | `#D4A528` | `#CDCDCD` |
| **Maximize** | `#28C840` | `+` (plus) / `⤢` (fullscreen) | `#1AAB29` | `#CDCDCD` |

**Interaction States**

| State | Appearance |
| :---- | :---- |
| **Default** | Solid colored circles, no glyphs visible. |
| **Hover (any button)** | All three buttons simultaneously show their glyphs. Glyph color: `#4D0000` (close), `#6B4400` (minimize), `#003D00` (maximize). Glyph fade-in: 80ms `ease-out`. |
| **Pressed** | Button fill shifts to pressed color (see table above). Glyph remains visible. |
| **Inactive window** | All three buttons display as `#CDCDCD` with no glyphs. On hover, colors and glyphs restore. |
| **Glyph font** | Rendered as vector paths (not a font) to ensure pixel-perfect rendering at all DPI levels. Stroke width: 1.5 DIP. |

## **5D. Animation Specifications**

| Animation | Duration | Easing | Details |
| :---- | :---- | :---- | :---- |
| **Genie minimize** | 400ms | `cubic-bezier(0.2, 0, 0, 1)` | Bitmap captured via DwmRegisterThumbnail; animated along a quadratic bezier curve toward Dock icon position. See Section 4B for full pipeline. |
| **Genie restore** | 350ms | `cubic-bezier(0.2, 0, 0, 1)` | Reverse of minimize — bitmap expands from Dock icon to window's restored position, then ShowWindow(SW\_SHOW). |
| **Dock icon bounce** | 900ms total | Up: `ease-out`, Down: `ease-in` | 3 bounces × 300ms. Translation: 12 DIP vertical. See Section 5B icon states. |
| **Dock show** | 250ms | `cubic-bezier(0.16, 1, 0.3, 1)` | Slide up from below screen edge. |
| **Dock hide** | 200ms | `cubic-bezier(0.7, 0, 0.84, 0)` | Slide down below screen edge. |
| **Traffic light glyph** | 80ms | `ease-out` | Opacity 0 → 1 for glyph paths. |
| **Menu bar hover** | 120ms | `ease-out` | Background highlight fade-in. |
| **Window open** | 250ms | `cubic-bezier(0.16, 1, 0.3, 1)` | Scale from 0.92 → 1.0 with opacity 0 → 1. |

## **5E. macOS → Windows Visual Mapping**

| macOS Visual Element | Windows Implementation in Harbor |
| :---- | :---- |
| **Menu bar translucency** | WPF Window with `SetWindowCompositionAttribute` using `ACCENT_ENABLE_ACRYLICBLURBEHIND`. Fallback: `DwmSetWindowAttribute` with `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (Acrylic). |
| **Dock frosted glass** | Same Acrylic API as menu bar, with higher blur radius (40px). Shell uses `AllowsTransparency=True` and a `VisualBrush` clipped to the Dock's rounded rectangle. |
| **SF Pro font** | `Segoe UI Variable` — Microsoft's variable font with similar optical sizing. Installed by default on Windows 11. |
| **Traffic light buttons** | WPF `Ellipse` elements with `Fill` bound to button state. Glyphs rendered as `Path` geometries inside each ellipse. |
| **Genie effect** | `DwmRegisterThumbnail` for bitmap capture → animated on a WPF `Canvas` overlay using `CompositionTarget.Rendering` for frame-synced updates. |
| **Dock magnification (hover)** | WPF `ScaleTransform` with `RenderTransformOrigin` at icon center. Animated via `DoubleAnimation` on `ScaleX`/`ScaleY`. |
| **System tray icons** | ManagedShell `TrayService` — natively hosts Shell_NotifyIcon items. Icons rendered at 18×18 DIP. |
| **Dark/Light mode** | Reads `AppsUseLightTheme` registry key (see Section 9B). Harbor's resource dictionaries swap between dark and light `SolidColorBrush` / `AcrylicBrush` sets. |
| **Window open animation** | `Storyboard` combining `DoubleAnimation` on `ScaleTransform` (0.92→1.0) and `Opacity` (0→1), applied to a fullscreen transparent overlay hosting a `DwmRegisterThumbnail` capture. |
| **Rounded corners** | Handled natively by Windows 11 DWM (`DwmSetWindowAttribute` with `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`). No Harbor intervention needed for app windows. Dock container uses `Border.CornerRadius="16"`. |

# **6\. Edge Cases & Application Compatibility**

## **6A. Per-Application UIA Tree Variability**

The UIA tree structure varies dramatically across UI frameworks. The document's overlay strategy assumes a discoverable TitleBar control type, but this assumption does not hold universally.

| Framework | UIA Behavior | Harbor Strategy |
| :---- | :---- | :---- |
| **Win32 / WPF** | Exposes full UIA tree with ControlType.TitleBar, Minimize/Maximize/Close buttons as children. | Primary path. Use UIA BoundingRectangle directly. |
| **UWP / WinUI 3** | Exposes TitleBar element but apps using ExtendContentIntoTitleBar draw custom chrome. BoundingRectangle may report zero-height. | Fallback to DwmGetWindowAttribute with DWMWA\_CAPTION\_BUTTON\_BOUNDS to locate the caption button region, then infer title bar height. |
| **Electron** | Many Electron apps use frameless windows with custom title bars rendered in HTML. UIA tree shows FrameworkId 'Chrome' with no TitleBar node. | Detect process name (electron.exe) or FrameworkId. Use GetWindowRect minus GetClientRect to compute NONCLIENT height. If NONCLIENT is zero (frameless), skip overlay for that window. |
| **Java (Swing/AWT)** | Java Access Bridge exposes UIA elements, but tree quality is inconsistent. TitleBar may be missing or mispositioned. | Fallback to NONCLIENT heuristic. Detect java.exe/javaw.exe process and use GetSystemMetrics(SM\_CYCAPTION) for title bar height. |
| **Qt** | Qt implements its own accessibility layer. Standard windows expose TitleBar; QML custom chrome may not. | Same NONCLIENT fallback as Electron. Maintain an allow/deny list per application for overlay behavior. |

**NONCLIENT Fallback Algorithm**

When UIA discovery fails to find a TitleBar element, Harbor computes the title bar rectangle heuristically:

titleBarRect.X \= windowRect.Left

titleBarRect.Y \= windowRect.Top

titleBarRect.Width \= windowRect.Width

titleBarRect.Height \= windowRect.Top \- clientRect.Top  // NONCLIENT height

If the computed NONCLIENT height is zero or negative, the window uses custom chrome and Harbor should not attach an overlay. These windows are added to a session-scoped skip list.

## **6B. Fullscreen & Exclusive Mode Applications**

Games and media players using exclusive fullscreen or borderless-fullscreen will conflict with AppBar reservations and overlays.

**Detection Strategy**

* Monitor foreground window dimensions against the display's full resolution. If the foreground window exactly matches the display bounds and has no visible NONCLIENT area, classify it as fullscreen.

* Additionally check for DXGI exclusive mode by querying the window's extended styles for WS\_EX\_TOPMOST combined with a matching-resolution client rect.

**Retreat Behavior**

* When a fullscreen application is detected, Harbor hides all overlays for that display, collapses the AppBar reservation (freeing the reserved screen edge), and suppresses EVENT\_OBJECT\_LOCATIONCHANGE processing for that HWND.

* When the fullscreen application exits or loses focus, Harbor restores AppBar reservations and re-enables overlay tracking.

# **7\. Overlay Synchronization (Critical Path)**

**Overlay synchronization is the single hardest engineering challenge in Harbor. The overlay must track the target window's position at display refresh rate. Any lag between the real title bar and the overlay during a window drag will be immediately visible and jarring to users.**

## **7A. The Problem**

EVENT\_OBJECT\_LOCATIONCHANGE fires asynchronously and at inconsistent intervals across applications. Some applications (notably Chromium-based apps) batch location-change events. WPF's layout engine introduces additional latency: dispatching a position update through the WPF Dispatcher, computing layout, and rendering can add 2–8ms per frame. On a 60Hz display, the total frame budget is 16.6ms. Losing even 4ms to dispatch overhead means the overlay visibly lags during fast drags.

## **7B. Mitigation Architecture: Dual-Layer Tracking**

Harbor implements a two-layer synchronization strategy:

**Layer 1: Event-Driven (Primary)**

* SetWinEventHook with EVENT\_OBJECT\_LOCATIONCHANGE remains the primary trigger.

* On event receipt, Harbor calls GetWindowRect on the target HWND (not UIA — GetWindowRect is a direct Win32 call with sub-millisecond latency) and repositions the overlay using SetWindowPos with SWP\_NOACTIVATE | SWP\_NOZORDER.

* Critical: The overlay position update must bypass the WPF layout system entirely. Use HwndSource.Handle with direct P/Invoke to SetWindowPos rather than setting WPF Window.Left/Top properties.

**Layer 2: High-Frequency Polling (Fallback)**

* A dedicated background thread polls GetWindowRect for tracked windows at \~120Hz (8.3ms interval) using a high-resolution timer (timeBeginPeriod(1) or Multimedia Timer).

* If the polled position differs from the last known position, the overlay is repositioned immediately via the same P/Invoke path.

* This catches cases where EVENT\_OBJECT\_LOCATIONCHANGE is delayed or suppressed (e.g., some games, Wine/Proton applications, or custom window managers).

**Frame Synchronization with DWM**

To minimize visual tearing between the overlay and the target window, Harbor should synchronize overlay updates with the Desktop Window Manager's composition cycle:

* Call DwmFlush() on the overlay update thread after repositioning. DwmFlush blocks until the next DWM composition event, ensuring the overlay's new position is picked up in the same composition frame as the target window's move.

* Use DwmGetCompositionTimingInfo to retrieve the precise VBlank timestamp and schedule overlay updates to arrive just before the next composition deadline.

## **7C. Performance Budget**

| Operation | Target Latency | Notes |
| :---- | :---- | :---- |
| GetWindowRect call | \< 0.1ms | Direct kernel call, negligible |
| SetWindowPos (overlay) | \< 0.5ms | Must bypass WPF Dispatcher |
| DwmFlush | \~8ms average (blocks) | Aligns to next compositor frame |
| Total per-frame budget | \< 16.6ms (60Hz) | Must leave headroom for application rendering |

## **7D. Escape Hatch: DirectComposition Overlay Layer**

If WPF proves fundamentally unable to achieve acceptable synchronization (the primary risk), Harbor should be architected to swap the overlay rendering layer. The overlay windows could be reimplemented using Windows.UI.Composition (the Visual Layer recommended by Microsoft as the successor to DirectComposition) or raw DirectComposition. These APIs operate independently of the UI thread and achieve native compositor-level frame rates. The tradeoff is significantly more complex C++/C\# interop and loss of WPF's declarative XAML tooling for the overlay controls.

**Decision Point:** Prototype the WPF overlay approach first. If drag-tracking latency exceeds 2 frames (33ms at 60Hz) in profiling, escalate to the DirectComposition fallback. Budget two additional sprints for this contingency.

# **8\. Multi-Monitor & DPI Scaling**

## **8A. Per-Monitor DPI Awareness**

On mixed-DPI setups (common with laptops connected to external displays), overlay positioning math will silently produce incorrect results unless the Harbor process is declared per-monitor DPI aware. Without this, Windows transparently scales coordinates through a virtualization layer, causing overlays to appear offset or incorrectly sized when a window moves between monitors.

**Required Configuration**

* Set DPI\_AWARENESS\_CONTEXT\_PER\_MONITOR\_AWARE\_V2 in the application manifest (app.manifest), not via runtime API calls. The manifest declaration is the only reliable method that takes effect before any window is created.

* On every EVENT\_OBJECT\_LOCATIONCHANGE, retrieve the DPI for the monitor hosting the target window using GetDpiForWindow(targetHwnd). Scale all overlay position and size calculations by (currentDpi / 96.0).

* When a window is dragged across a monitor boundary (detected by a change in MonitorFromWindow return value), destroy and recreate the overlay on the new monitor's DPI context. WPF windows cannot change their DPI context after creation.

## **8B. Multi-Monitor AppBar Behavior**

* Register separate AppBar instances for each connected monitor. The Top Menu and Dock should appear on the primary monitor by default, with a user preference to extend to all monitors.

* Handle WM\_DISPLAYCHANGE to detect monitor connect/disconnect events and rebuild AppBar registrations dynamically.

# **9\. Title Bar Color Detection**

Harbor must draw a solid block on the right side of the overlay to obscure native window controls. This block must match the title bar's background color, or it will appear as a visible rectangle to the user.

## **9A. Detection Strategy (Cascading)**

**Priority 1 — DwmGetColorizationColor:** Query the system-wide DWM colorization. This works for all applications using the default Windows title bar (the majority of Win32 apps). Returns a single ARGB value.

**Priority 2 — Mica/Acrylic Detection:** For Windows 11 apps using Mica or Acrylic material, the title bar is semi-transparent. Harbor should detect DWM backdrop type via DwmGetWindowAttribute(DWMWA\_SYSTEMBACKDROP\_TYPE) and render a matching translucent block.

**Priority 3 — Pixel Sampling:** For apps with custom chrome (VS Code, Spotify, Discord, Slack), Harbor takes a screen capture of a small region (8x8 pixels) at the right edge of the title bar. Compute the median color and use it for the mask. Cache the result per-process and invalidate on WM\_THEMECHANGED.

**Priority 4 — User Override:** Expose a per-application color override in Harbor's settings. Users can manually specify the title bar color for problematic applications.

## **9B. Dark Mode / Light Mode**

* Subscribe to the Windows registry key HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\\AppsUseLightTheme. When this value changes, invalidate all cached title bar colors and re-query.

* Harbor's own shell chrome (Top Menu, Dock) should respect this setting and offer both dark and light themes.

# **10\. Crash Recovery & Graceful Degradation**

**Because Harbor replaces explorer.exe and actively hides windows (via SW\_HIDE during genie animations), an unhandled crash can leave the user with no taskbar, no system tray, and invisible windows. This section describes the safety net architecture.**

## **10A. Watchdog Process**

* Harbor launches a lightweight native watchdog process (harbor-watchdog.exe) on startup. This process is independent of the .NET runtime and is kept deliberately simple (\< 500 lines of C).

* The watchdog monitors the Harbor main process via a heartbeat mechanism: Harbor writes to a shared memory-mapped file every 500ms. If the watchdog detects 3 consecutive missed heartbeats (1.5 seconds), it executes the recovery sequence.

## **10B. Hidden Window Registry**

Harbor maintains a persistent registry of all window handles (HWNDs) that it has hidden via SW\_HIDE:

* Storage: A memory-mapped file at a well-known path (e.g., %TEMP%\\harbor-hidden-hwnds.dat) that is accessible to both the main process and the watchdog.

* Format: A simple array of HWND values, updated atomically on every hide/show operation.

* On Recovery: The watchdog (or Harbor itself on restart) iterates the registry and calls ShowWindow(SW\_SHOW) on every recorded HWND. Invalid handles (from processes that exited) are silently skipped using IsWindow() checks.

## **10C. Recovery Sequence**

When the watchdog detects Harbor has crashed:

* **1\.** Re-show all hidden windows from the Hidden Window Registry.

* **2\.** Restore native Windows animations (SPI\_SETCLIENTAREAANIMATION \= TRUE).

* **3\.** Launch explorer.exe as a fallback shell so the user has a functional taskbar.

* **4\.** Display a system notification informing the user that Harbor crashed and explorer.exe has been restored.

* **5\.** Write a crash dump to %LOCALAPPDATA%\\Harbor\\CrashDumps\\ for post-mortem analysis.

## **10D. Safe Startup**

* On launch, Harbor always checks for and clears any stale Hidden Window Registry entries from a previous session.

* Harbor registers an AppDomain.UnhandledException handler and a TaskScheduler.UnobservedTaskException handler that trigger the recovery sequence before process exit.

# **11\. Security & Performance**

## **11A. Security Posture**

| Aspect | Assessment |
| :---- | :---- |
| **EDR Compatibility** | High. Harbor uses no global hooks (SetWindowsHookEx), no DLL injection, and no process memory modification. AV/EDR engines classify UIA client usage as standard accessibility behavior, equivalent to a screen reader. |
| **Attack Surface** | Low. Harbor runs as a standard user-mode process. The watchdog runs with identical privileges. No kernel-mode components, no driver installation, no elevated privileges required. |
| **Data Handling** | Harbor reads only window geometry and control metadata via UIA. It does not read or log application content, keystrokes, or user data. |

## **11B. Performance Profile**

| Component | Overhead | Mitigation |
| :---- | :---- | :---- |
| **Overlay Tracking Loop** | Moderate | Bypass WPF Dispatcher for position updates. Use direct P/Invoke to SetWindowPos. Suspend tracking for minimized/occluded windows. |
| **UIA Tree Queries** | Low–Moderate | Cache BoundingRectangle results. Only re-query on EVENT\_OBJECT\_LOCATIONCHANGE, not on every frame. UIA queries can block if the target app is hung — set a 100ms timeout via UIA Condition. |
| **Genie Animation** | Burst (GPU) | DwmRegisterThumbnail is GPU-accelerated. Animation canvas is a short-lived overlay (\< 500ms). Negligible steady-state cost. |
| **Memory (Idle)** | \~60–100 MB | WPF runtime \+ ManagedShell \+ overlay windows. Comparable to explorer.exe baseline. |

# **12\. Testing Strategy**

## **12A. Overlay Accuracy Matrix**

Maintain a curated list of 20+ applications spanning all major UI frameworks. For each application, automated tests verify:

* UIA TitleBar element is discoverable (or fallback is triggered correctly).

* Overlay bounding rectangle matches the native title bar within a 2-pixel tolerance.

* Click-through on traffic-light buttons correctly triggers SC\_CLOSE / SC\_MINIMIZE / SC\_MAXIMIZE on the target window.

* The native window controls are fully obscured by the color-matched mask.

## **12B. Target Application List (Minimum)**

| Category | Applications | Framework |
| :---- | :---- | :---- |
| **Win32 Native** | Notepad, File Explorer, Paint | Win32 |
| **WPF** | Visual Studio, Blend | WPF |
| **UWP / WinUI** | Settings, Calculator, Windows Terminal | UWP / WinUI 3 |
| **Electron** | VS Code, Discord, Slack, Spotify | Chromium/Electron |
| **Java** | IntelliJ IDEA, Minecraft Launcher | Swing / JavaFX |
| **Qt** | VLC, Telegram Desktop | Qt Widgets / QML |
| **Fullscreen** | Any DirectX game, VLC fullscreen | DXGI Exclusive / Borderless |

## **12C. Synchronization Profiling**

* Instrument the overlay tracking loop with high-resolution timestamps (QueryPerformanceCounter).

* Measure the delta between GetWindowRect returning a new position and the overlay's SetWindowPos call completing. Target: \< 2ms p99.

* Record frame-miss rate during continuous window dragging at 60Hz. Target: \< 1% missed frames.

* Test on both integrated graphics (Intel UHD) and discrete GPUs (NVIDIA/AMD) to catch driver-specific DWM synchronization differences.

# **13\. Open Questions & Future Work**

* Global Menu Bar: Should Harbor attempt to read and replicate the active application's menu structure via UIA, providing a functional macOS-style global menu? This would significantly increase UIA query complexity and per-app compatibility surface.

* Window Snapping: Windows 11's Snap Layouts interact with NONCLIENT areas. Harbor's overlay may need to intercept or forward snap-related messages (WM\_NCHITTEST, WM\_GETMINMAXINFO) to preserve snap functionality.

* Rounded Corners: Windows 11 applies rounded corners via DWM. Harbor's overlay mask must account for corner radius to avoid visible right-angle artifacts over rounded title bars.

* Accessibility: Harbor itself must expose a valid UIA tree for its own shell chrome (Top Menu, Dock, Traffic Lights). Screen reader users must be able to navigate Harbor's controls.

* Auto-Update: Define an update mechanism that can replace Harbor binaries while the shell is running, or gracefully restart with minimal user disruption.

# **Appendix A: Key Win32 & DWM API Reference**

| API | Purpose in Harbor |
| :---- | :---- |
| **SetWinEventHook** | Subscribe to system-wide window events (foreground change, location change, create/destroy). |
| **GetForegroundWindow** | Determine which application is currently active for the Top Menu. |
| **GetWindowRect / GetClientRect** | Compute window and client area dimensions for NONCLIENT fallback. |
| **SetWindowPos** | Reposition overlay windows without focus stealing (SWP\_NOACTIVATE). |
| **DwmRegisterThumbnail** | Capture live window bitmap for genie animation. |
| **DwmFlush** | Synchronize overlay rendering with DWM composition cycle. |
| **DwmGetColorizationColor** | Read system-wide title bar accent color. |
| **DwmGetWindowAttribute** | Query per-window DWM properties (backdrop type, caption bounds). |
| **GetDpiForWindow** | Retrieve per-monitor DPI scaling for overlay position math. |
| **SHGetFileInfo** | Extract application icons for the Dock. |
| **SystemParametersInfo** | Disable/restore native window animations. |

