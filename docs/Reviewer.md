The reason your headphones fail to reconnect using `BluetoothSetServiceState` is that the function is technically a **service/driver manager**, not a radio-level controller. While calling `BLUETOOTH_SERVICE_DISABLE` successfully forces Windows to tear down the driver stack and drop the link, calling `BLUETOOTH_SERVICE_ENABLE` only marks the service as "allowed" in the registry and reinstalls the driver. It does **not** trigger a radio "page" or handshake to establish a new physical connection with the device.

To replicate the macOS "Connect" behavior on Windows 11, you must use a more aggressive approach that forces the OS to re-probe the device.

### 1. The PnP "Thump" Strategy (Recommended)

The absolute most reliable way to force a reconnection is to toggle the state of the **Plug and Play (PnP)** nodes associated with the device. When you enable a Bluetooth PnP node that was previously disabled, the `BthPort.sys` driver is forced to re-enumerate the device and initiate a radio-level connection.

* **Target All Nodes:** Bluetooth audio devices are often represented by multiple PnP entities (e.g., the base headset, and one or two "Avrcp Transport" nodes). You must toggle **all** of them to ensure the device connects as "Voice & Music".


* **Implementation:** Use the `SetupAPI` in C++ or `Disable-PnpDevice` / `Enable-PnpDevice` in a broker process. Note that this requires administrative privileges to modify hardware states.



### 2. The Winsock "Radio Wakeup" Trick

If you cannot run as administrator, you can use a Winsock-based technique to force a radio link. This involves attempting to open a raw socket to the device's address, which bypasses the high-level service state and forces the local Bluetooth radio to find the remote hardware.

* **Socket Connection:** Create a `SOCK_STREAM` using the `AF_BTH` address family and the `BTHPROTO_RFCOMM` protocol.
* **Target UUID:** Attempt to connect to the device's MAC address using a standard service UUID, such as the `HandsFreeServiceClass_UUID` (`0000111e-...`) or `AudioSinkServiceClass_UUID` (`0000110b-...`).
* **Outcome:** Even if the socket connection itself fails or is immediately closed, the attempt forces the Windows Bluetooth stack to establish a **Physical Link**. Once the physical link is active, Windows will automatically detect the enabled services and start the audio routing.



### 3. macOS-Like Logic Refinement

In macOS, "Disconnecting" from the menu bar does not actually disable the underlying services (it doesn't "uncheck the boxes"); it simply terminates the active session. On Windows, your shell replacement should adopt this tiered logic:

1. **For Disconnect:** Use `BluetoothSetServiceState(DISABLE)` as you currently do, as it is the most consistent way to force a drop without unpairing.
2. **For Connect:**
* **Step A:** Call `BluetoothSetServiceState(ENABLE)` to ensure the drivers are present.


* **Step B:** Perform a **Winsock Radio Wakeup** (connect an RFCOMM socket) to establish the physical link.
* **Step C (Fallback):** If the device is still not connected after 3 seconds, execute a **PnP Node Toggle** on all matching device IDs to force a full re-enumeration.





### Technical Considerations

* **Ghost Nodes:** If a device has been paired multiple times or has corrupted registry entries, `BluetoothSetServiceState` may return success while acting on a "ghost" device ID. Always verify the `BLUETOOTH_DEVICE_INFO` address matches the current physical device.


* **Timing:** After enabling a service, Windows can take up to 5–10 seconds to fully initialize the "Voice, Music" audio endpoints. Your context menu should provide visual feedback (e.g., a "Connecting..." spinner) until the `ConnectionStatus` property of the `BluetoothDevice` object transitions to `Connected`.