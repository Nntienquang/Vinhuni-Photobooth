# Sony A7 III / ILCE-7M3

Current capability: **DependencyMissing**. This project contains no Sony USB/PTP transport. Selecting Sony is persisted, but connect, live view and capture deliberately fail with an actionable message. A model name is not proof of detection: scanning currently returns no Sony devices. Imaging Edge is never invoked or required by the booth.

## Missing integration

Sony lists ILCE-7M3 under [Camera Remote Command](https://support.d-imaging.sony.co.jp/app/cameraremotecommand/en/index.html). Obtain Sony's Camera Remote Command documentation/sample package, accept its license, and implement or integrate a Windows x64 USB/PTP transport for this model behind `SonyCameraProvider`.

The missing component must enumerate attached devices, open/close PC Remote sessions, transfer live-view JPEG frames, release the shutter, download the complete still JPEG atomically to the requested local path, and report USB removal/cancel pending transfers. There is **no drop-in DLL installation or environment variable that enables this unfinished transport**. Installing Camera Remote SDK alone is not an implementation of this provider. Verify model compatibility before choosing any library.

Keep the Windows driver setup already verified with this camera; do not replace drivers speculatively. Before hardware validation:

1. Set USB Connection to PC Remote and configure JPEG capture/PC transfer. See [Sony's ILCE-7M3 guide](https://helpguide.sony.net/ilc/1720/v1/en/contents/TP0001667702.html).
2. Close Imaging Edge Remote and other applications holding the camera.
3. Connect the USB data cable, select Sony in Admin > Settings > Camera & capture.
4. Until a transport is implemented, expect DependencyMissing, no live frames and no test image.
5. After integration, validate scan/connect, live view, full-resolution capture, cable removal during capture, and reconnect on this physical camera before event use.

No Sony hardware control has been claimed or simulated by this change.
