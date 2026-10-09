# Rollback to direct frame assignment

Restored customer flow: template -> capture six -> frame-first assignment of four
-> GHÉP ẢNH -> final preview/confirm -> existing printing. No filter or customer QR
screen is part of this flow.

## Reverted changes

- `apps/web/src/pages/Booth.tsx`: removed filter screen, automatic FILTER
  transition and filter requests; restored SlotPhoto and select-then-compose.
  Removed customer QR rendering; print controls remain.
- `apps/web/src/boothFlow.ts`: removed filter draft fields and FILTER phase;
  retained session/template/take-scoped assignment persistence and count helpers.
- `apps/bridge/BoothEngine.cs`: capture finishes in PHOTO_SELECTION again;
  select-then-compose works directly; composition failure returns to assignment.
  Removed new assignment/filter methods and filter metadata use.
- `apps/bridge/Program.cs`: removed the three added assignment/filter endpoints
  and filter request metadata. Startup and camera routes were not changed.
- `apps/bridge/PhotoComposer.cs`: removed the preset argument and filter pass,
  restoring original JPEG composition with the frame drawn last.
- `apps/bridge/Models.cs`, `packages/shared/src/index.ts`: removed preset metadata
  and the added assignment validator. ASSIGN/FILTER enum/string values remain only
  to read existing session JSON and accept transitional sessions; new sessions do
  not enter them. Both map directly to the restored assignment UI.
- Removed PhotoFilters.cs, FilterPicker.tsx, photoFilters.ts, preset/vector JSON,
  their filter tests and assignment-transition tests. Removed corresponding
  embedded/copy resources from both .csproj files and filter CSS from style.css.
- Updated boothFlow.test.ts and booth.spec.ts for direct compose; added
  restored-flow.spec.ts to verify the requested sequence on a real webcam.
- Rebuilt frontend dist/wwwroot and the Release bridge.

Providers, Windows printing, frame/duplicate-strip editors, current template data,
slot geometry, slot assignment helpers and the shot/-1 fix are unchanged.
Tests use `.tools/rollback-verify`; the custom template was copied there without
modifying its source. Existing customer sessions were not deleted or rewritten.

## Failure findings and limits

- The introduced bridge state ASSIGN was incompatible with older customer JS
  bundles, whose assignment branch only handled PHOTO_SELECTION. Those bundles
  have no content branch for ASSIGN/FILTER, causing an apparently blank customer
  page when paired with the newer bridge. The old bundle remains on disk and
  contains neither state; the PWA uses prompt-based updates. This is an identified
  version-mismatch failure mechanism, not proof of the exact bundle in the user's
  browser: no browser stack trace or cache snapshot from that failure was available.
- The added FILTER-only compose guard rejected direct composition from ASSIGN.
  Combined with the added automatic assignment endpoint, it made the former direct
  flow incompatible. The latest real-data session inspected had six shots and
  ASSIGN state with no error, consistent with a post-capture UI/transition failure.
- The two recent ERROR sessions inspected explicitly contained
  `Bridge restarted; originals preserved`, including one with six photos and one
  with zero. Existing startup recovery marks unfinished sessions ERROR on restart;
  it does not resume them or delete their originals. There is no evidence that
  filtering deleted six captures from the zero-photo session. Startup recovery and
  stored customer session files were deliberately not rewritten.
- Logs also show stale webcam submissions while another provider was selected,
  plus missing default-template PNG requests. The current custom template exists.
  These findings were not used as a reason to modify camera providers or templates.

## Verification

- 64 bridge tests passed; 29 web tests passed.
- Web production build and lint passed. Existing SignalR annotation warnings only.
- Release bridge build passed with zero warnings/errors.
- Physical webcam was opened with getUserMedia in Edge: 640 x 480 input, no fake
  media flag or injected frames. Browser-driven verification captured exactly six,
  used the current custom frame copy, reloaded partial assignments, assigned four
  photos, verified derived right slots, clicked GHÉP ẢNH and reached FINAL_PREVIEW
  with `error: null`, six originals and selected order `[2,4,0,5]`.
- Session: `98877d50aeb9484987b491832fee0b3c` in isolated test data.
- Assignment and final screenshots were visually inspected:
  `.tools/webcam-restored-assignment.png`, `.tools/webcam-restored-preview.png`.
  Frames from the physical camera were very dark; this verifies flow rather than
  lighting/exposure. It was a browser-driven physical-device check, not a claim
  that a human manually operated the laptop.
- Four existing browser regressions passed, including mock printing, retake,
  reconnect, dashboard and editor rendering. Together with the physical-webcam
  check, five browser tests passed. No physical print job was sent.

Close existing booth/PWA tabs and reopen after restarting the bridge with the
rebuilt binaries, so an old in-memory frontend cannot remain paired with a newer
backend. No service-worker update architecture was introduced.
