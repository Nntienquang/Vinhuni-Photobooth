# Customer photo filters

Historical report: this feature has been rolled back. See `booth-rollback.md` for
the current direct-assignment flow. The implementation described below is no
longer active.

## Pipeline and insertion point

Inspection found that capture providers write original JPEGs, the customer assigns
photos during `PHOTO_SELECTION`, and `BoothEngine.ComposeCore` passes those paths
to the existing SkiaSharp `PhotoComposer`. Moving final composition to the browser
would change the architecture. Filtering is therefore inserted in two places:

1. Canvas 2D previews in the existing assignment screen.
2. `PhotoComposer`, immediately after decoding/orienting each original and before
   cover cropping, mirroring, rotation, clipping and slot placement.

The PNG frame is drawn last with the existing unfiltered paint. JPEGs and frame
files are opened only for reading. No filtered intermediate photo is persisted.
Output dimensions, DPI, encoder settings, camera providers, frame editor, cloud
and Windows printing are unchanged.

## Reference review

Reviewed https://github.com/skedgeloop/auralens and specifically
`src/lib/imageFilters.js`: Canvas ImageData processing, small per-preset preview
generation, and resetting to an original source. No repository, source functions,
preset coefficients, assets or dependencies were copied. This implementation uses
independently authored fixed-point color matrices. No AI dependencies were added.

## Presets and metadata

`packages/shared/filter-presets.json` is the single preset definition, imported by
the browser and embedded into the bridge assembly. IDs are stable; keep existing
coefficients immutable if introducing future versions. Presets are Original,
Bright/Clean, Warm, Film, Vintage, Black & White and Cinematic. Film uses a softened
tonal palette without random grain.

The customer's local assignment draft holds `filterPreset`. The existing
`POST /api/session/{id}/select` accepts it alongside `indices`, validates it before
mutating the session, then stores `Session.FilterPreset` with the selection.
Existing `Storage.Save` persists it in `session.json` and the SQLite session JSON.
The filter adds only this preset ID to persistent session state. Preview images
are disposable browser cache entries, never session fields or API uploads.

Legacy sessions and new sessions default to `original`. Reordering preserves the
current preset. Retake resets to Original. Failed composition preserves metadata
for retry. Existing state guards prevent changes after leaving photo selection.

## Performance and fidelity

- Canvas working size is at most 320 pixels on the longest edge, never upscaled.
- Seven previews are produced from one decoded photo and an untouched source
  pixel buffer; changing presets cannot accumulate filters.
- The per-screen LRU cache holds at most 12 photo/take URLs (84 small PNG previews).
  Pending promises are reused; decoding is serialized. Cached entries contain no
  full-resolution bitmap. Failed work is evicted so subsequent requests can retry.
- Cache identity includes session, photo and retake. Async results from old UI
  requests are ignored. The cache becomes collectible when the screen unmounts;
  already queued work may finish before collection.
- Preview work yields between presets. Original slot rendering uses the original
  URL directly. Final Original returns without a pixel pass.
- Final processing is a single integer pass over the full oriented bitmap, inside
  the composer's existing background task. It retains the existing one-photo-at-a-
  time memory behavior; duplicated slots currently decode/filter again, as they
  already decoded again before this change. No permanent full-size cache is added.
- Browser and bridge use identical coefficients, explicit rounding and clamping.
  Shared golden vectors verify equal pixel transforms. Browser JPEG decoding,
  preview downsampling and final resampling/encoding can cause small differences
  at edges or clipped highlights; thumbnails are not pixel-exact final exports.
- Automated tests are not an all-day laptop soak test or a physical print/color
  calibration test.

## Files changed

New source: `apps/bridge/PhotoFilters.cs`, `apps/web/src/photoFilters.ts`,
`apps/web/src/FilterPicker.tsx`, `packages/shared/filter-presets.json`.

Integration: `apps/bridge/PhotoComposer.cs`, `apps/bridge/BoothEngine.cs`,
`apps/bridge/Models.cs`, `apps/bridge/Program.cs`,
`apps/bridge/Booth.Bridge.csproj`, `apps/web/src/pages/Booth.tsx`,
`apps/web/src/style.css`, `packages/shared/src/index.ts`.

Tests: `apps/web/src/photoFilters.test.ts`, `apps/web/tests/filters.spec.ts`,
`tests/Bridge.Tests/PhotoFilterTests.cs`, `tests/Bridge.Tests/Bridge.Tests.csproj`,
`packages/shared/filter-vectors.json`.

Documentation: this file and `docs/filter-picker-preview.png`.
Generated web build artifacts: `apps/web/dist` and copied `apps/bridge/wwwroot`.

## Validation

- `dotnet test tests/Bridge.Tests --no-restore`: 68 passed.
- `node scripts/pnpm-run.mjs --filter @booth/web test`: 37 passed.
- `node scripts/pnpm-run.mjs --filter @booth/web lint`: passed.
- `node scripts/pnpm-run.mjs --filter @booth/web build`: passed; existing SignalR
  annotation warnings only.
- `dotnet build apps/bridge -c Release --no-restore`: passed, zero warnings/errors.
- `node scripts/pnpm-run.mjs --filter @booth/web exec playwright test tests/filters.spec.ts`:
  passed in headless Edge with an isolated data directory and mock camera/printing.

Coverage includes determinism, original buffer/file preservation, Original no-op,
shared preview/final pixel vectors, frame isolation, full output dimensions,
metadata round-trip/defaults, invalid presets, cache reuse/eviction/failure retry,
live customer thumbnails, session-wide selection and post-composition locking.
