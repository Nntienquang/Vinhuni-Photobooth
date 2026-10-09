# Runtime camera change report — 2026-10-07

## Files changed

- `apps/bridge/Camera/ICameraProvider.cs`: normalized scanning API; existing JPEG-path capture contract retained.
- `apps/bridge/Camera/RuntimeCameraProvider.cs`: provider ownership, serialization, persistence, Auto Detect and event forwarding.
- `apps/bridge/Camera/SonyCameraProvider.cs`: explicit DependencyMissing boundary, no simulated hardware behavior.
- `apps/bridge/Camera/MockCameraProvider.cs`: explicit mock device scanning.
- `apps/bridge/Camera/CanonEdSdkCameraProvider.cs`: scan adapter.
- `apps/bridge/Camera/CanonEdSdkService.cs`: Canon enumeration, R50 V / RP detection, disconnect on worker failure.
- `apps/bridge/Models.cs`: camera devices, preferences and status metadata.
- `apps/bridge/BoothEngine.cs`: session guards, startup preferences, safe disconnect and same-provider recovery.
- `apps/bridge/Program.cs`: runtime registration and camera routes.
- `packages/shared/src/index.ts`: shared device/preferences/status contracts.
- `apps/web/src/pages/CameraSettings.tsx`: provider selector, startup toggles, device information and actions.
- `apps/web/src/pages/Admin.tsx`: existing settings integration and dynamic dashboard device display.
- `apps/web/src/api.ts`: clear stopped/disconnected live-view frames.
- `tests/Bridge.Tests/CameraTests.cs`: 17 new camera test cases.
- `apps/web/tests/camera.spec.ts`: Admin and API runtime-switch regression.
- `README.md`, `docs/API.md`, `docs/CANON_SETUP.md`, `docs/SONY_SETUP.md`, `docs/RUNTIME_CAMERAS.md`, this report: setup, API, limitations and validation.
- Generated frontend output rebuilt in `apps/web/dist` and copied into `apps/bridge/wwwroot`; TypeScript build metadata and bridge build outputs refreshed.

## Validation results

- Bridge tests: **43 passed** (26 existing + 17 new).
- Frontend unit tests: **6 passed**.
- Playwright/Edge: **5 passed** (all 4 existing + 1 new).
- Frontend production build: passed. Rollup emits existing third-party SignalR annotation notices.
- Bridge Release build: passed, **0 warnings / 0 errors**.
- Frontend ESLint: passed.

Browser regression used isolated `data/runtime-camera-validation`, explicit Mock camera and Mock printer; no event session data was removed and no real print was submitted. The temporary validation bridge was stopped afterward.

Coverage includes provider disposal/switching, persistence, paused-session protection, Canon model matching, Canon-first Auto Detect, Sony dependency errors, no automatic Mock fallback, reconnect, the existing customer capture/retake/composition/print flow, and disconnect during countdown with an already saved shot remaining accessible.

Physical Canon R50 V/RP and Sony USB operation were not validated. Sony control requires implementation of the dependency described in SONY_SETUP.md; merely installing DLLs does not enable it. See RUNTIME_CAMERAS.md for operator steps.
