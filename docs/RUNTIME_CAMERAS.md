# Runtime camera operation

Use Admin > Settings > Camera & capture. Changing Camera Provider immediately saves the selection and attempts a connection. Options: Auto Detect, Canon EDSDK, Sony, Mock. No bridge restart is needed for provider changes.

- Canon: select Canon EDSDK. The existing EDSDK implementation now recognizes EOS R50 V and EOS RP. Install the licensed x64 Canon SDK as described in CANON_SETUP.md, close EOS Utility, and connect the camera by USB. First compatible Canon in SDK enumeration order is selected.
- Sony: select Sony. See SONY_SETUP.md for the currently missing ILCE-7M3 transport. DependencyMissing is expected; no fake connection or capture is provided.
- Mock: explicitly select Mock to generate sample images.
- Auto Detect: tries supported Canon first, then Sony; never selects Mock. Missing SDK diagnostics remain visible. With the current Sony boundary, Sony cannot be enumerated or connected.

SCAN DEVICES lists supported devices through the selected provider. CONNECT opens the selected provider; RECONNECT tears it down and opens a fresh instance. DISCONNECT releases ownership. TEST LIVE VIEW starts streaming; open Dashboard to see the monitor. TEST CAPTURE saves a full JPEG under the data directory's `originals/test_<id>.jpg` and displays its local path in the notification. These controls never delete session photos.

Auto-connect on startup defaults to ON; disabling it still scans at startup but does not open a camera session. Start live view on connect is independently configurable. Preferences are stored atomically in `<PHOTOBOOTH_DATA_DIR>/camera-settings.json`, separate from capture/print settings. Existing explicit CAMERA_PROVIDER=mock/canon/sony is used only when no saved camera preferences exist; otherwise the default is Auto Detect.

The existing PAUSE button uses the MAINTENANCE state. Switching and camera tests require IDLE or MAINTENANCE **with no current session**. Emergency DISCONNECT remains available during a session: it cancels capture, waits for transfer cancellation and preserves saved shots. CONNECT / RECONNECT can recover the same provider in MAINTENANCE even while the interrupted session remains saved. Pausing an active session does not bypass the restriction: finish it or use START / RESET before changing cameras. Reset ends the current in-memory session and leaves its saved originals/final intact. Capture and switching use the same booth lock.

USB removal stops Canon live view, cancels any pending capture, preserves existing shots, and notifies Admin/customer displays through the existing SignalR status channel. After ending the interrupted session, click RECONNECT, then START / RESET to reopen the booth. Automatic reconnect is not enabled.

Hardware acceptance remains necessary for both Canon models and a future Sony implementation; automated tests use Mock/fake providers and cannot establish physical USB reliability.
