Harbor Shell Replacement: Architecture & Design Document (Overlay Model)

1. Executive Summary
Harbor is a custom Windows 11 shell replacement designed to provide a macOS-style desktop paradigm. The MVP will replace explorer.exe to render a persistent Top Menu bar and a dynamic Dock. To emulate macOS window management safely, Harbor utilizes the Windows UI Automation API to map application windows and deploy borderless, transparent WPF overlays. These overlays intercept clicks and draw macOS-style "traffic light" controls without modifying the target application's memory or triggering Endpoint Detection and Response (EDR) alerts.

2. Technical Stack
Language: C# (.NET 8 or .NET 9).

Core Shell Library: ManagedShell (handles AppBar screen reservation, system tray interception, and task enumeration).

UI Framework: WPF (Windows Presentation Foundation).

Automation Framework: UIAutomationClient (UIAutomationTypes). The native Windows API used by screen readers to map out the bounding boxes of every button and window on the screen.

3. Core MVP Components
A. The Top Menu (System Bar)
Implementation: A WPF Window registered via ManagedShell as a top-docked AppBar.

Left Side (Global Menu): Reads the foreground window using GetForegroundWindow(), displaying the active application name.

Right Side (System Tray): Utilizes ManagedShell’s TrayService to natively host standard Windows notification area icons (network, volume, background tasks).

B. The Dock
Implementation: A WPF Window registered as a bottom-docked AppBar.

Behavior: Bound to ManagedShell’s TaskService. Dynamically updates based on EVENT_OBJECT_CREATE and EVENT_OBJECT_DESTROY system events.

Interaction: Extracts high-resolution icons from active executables via SHGetFileInfo.

4. Emulating macOS Window Controls (The UIA Overlay Layer)
This phase relies on treating the entire Windows OS like a massive DOM or widget tree. Every window, title bar, and button is a node.  Harbor reads this tree to position its custom UI on top of existing applications.

A. Left-Aligned Window Action Buttons (Traffic Lights via Overlay)
Instead of modifying the target app, Harbor draws a "mask" over it.

The Approach (UIA Tracking): Harbor subscribes to SetWinEventHook to listen for EVENT_SYSTEM_FOREGROUND and EVENT_OBJECT_LOCATIONCHANGE. When an application becomes active or moves, Harbor uses the UI Automation API to find the exact bounding rectangle (X, Y, Width, Height) of that application's native title bar.

The Execution: 1. Harbor spawns a borderless, transparent WPF Window structured as an overlay (WS_EX_LAYERED | WS_EX_NOACTIVATE). This ensures clicking the overlay doesn't steal focus from the underlying app.
2. Harbor matches the overlay's size and position to the native title bar.
3. On the right side of the overlay, Harbor draws a solid block matching the title bar's background color to physically obscure the native Windows minimize/maximize/close buttons.
4. On the left side of the overlay, Harbor draws the interactive macOS "traffic lights".
5. When a user clicks Harbor's custom close button, Harbor programmatically sends an SC_CLOSE message to the underlying window handle.

B. Custom Minimize/Maximize Animations (The "Genie" Effect)
Because Harbor controls the click event on the overlay buttons, it controls the animation trigger.

The Approach: Disable native Windows animations via Registry (SystemParametersInfo with SPI_SETCLIENTAREAANIMATION).

The Execution: When the user clicks Harbor's custom yellow "Minimize" overlay button:

Harbor captures a live bitmap of the target window using the DwmRegisterThumbnail API.

Harbor immediately sends a ShowWindow(SW_HIDE) command to the native window to make it vanish instantly.

Harbor spawns an animation canvas overlay covering the desktop.

Harbor animates the bitmap thumbnail, calculating the bezier curve to shrink it down to the precise coordinates of its corresponding Dock icon.

5. Security & Performance Trade-offs
Security: High. This approach completely bypasses global hooks (SetWindowsHookEx) and DLL injection. AV engines will view Harbor as standard accessibility software reading the screen, rather than a malicious actor attempting to rewrite process memory.

Performance: Moderate Overhead. While injection is highly performant (the app draws itself), the overlay approach requires a continuous tracking loop. Harbor must listen to EVENT_OBJECT_LOCATIONCHANGE so that when a user drags a window across the screen, the WPF overlay moves at the exact same refresh rate. If the synchronization logic is poorly optimized, the overlay buttons will noticeably lag behind the window as it moves.