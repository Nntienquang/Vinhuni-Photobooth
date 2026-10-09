# Frame-first booth flow restoration

Historical report: the ASSIGN/FILTER transition changes below have been rolled
back. See `booth-rollback.md` for the current behavior.

## Findings

The inspected TypeScript source already contained frame-first assignment; the old
generic gallery/reorder page was not in that source. The actual state-machine gap
was that assignment and filtering were combined under `PHOTO_SELECTION`, with a
direct compose action and no separate FILTER stage. Capture itself already used
`EffectiveCaptureCount`, independently of the logical selection count.

Several obsolete JS bundles containing the gallery/reorder path remain in
`apps/bridge/wwwroot/assets`. The index inspected before this change referenced
the newer filter bundle, not those old bundles. The PWA uses prompt-based updates.
An already-open/cached frontend is a plausible explanation for the reported old
screen, but this session did not establish which bundle the user's browser ran.
The web app has been rebuilt and copied to the bridge's existing static directory.
Existing open PWA tabs may need to be closed and reopened to activate the update.
No service-worker policy or bridge startup logic was changed.

## Final order

Customer phases:

`TEMPLATE -> CAPTURE (6) -> ASSIGN (4 logical slots) -> FILTER -> COMPOSE -> CONFIRM -> existing print/QR completion -> THANK_YOU`

Internal bridge states retain their established names for protected subsystems:

`FRAME_SELECTION -> LIVE_VIEW/READY/COUNTDOWN/CAPTURING/BETWEEN_SHOTS -> ASSIGN -> FILTER -> COMPOSING -> FINAL_PREVIEW -> PRINTING/QR_READY -> THANK_YOU`

The existing auto-print/manual-print choice and cloud/QR timing are unchanged.
`customerPhase` names the template, capture, composition and confirmation phases
without renaming the camera or printing states. ASSIGN and FILTER are explicit,
persisted bridge states using existing status publication.

After the final capture the customer sees the real frame overlay and actual slots.
Clicking a photo fills the active logical slot and advances to the next empty one.
The right duplicate strip derives from the same assignments. Completing all four
slots automatically saves slot order and transitions to FILTER. A retry/continue
button is available if the transition fails. There are no reorder controls.

FILTER retains the existing session-wide presets and preview cache. Back returns
to ASSIGN without clearing the draft; the customer can replace a filled slot.
Composition is only accepted in FILTER, and failed composition returns there.
Current ASSIGN/FILTER sessions cannot use legacy select/reorder endpoints. Legacy
PHOTO_SELECTION status renders the same frame-first page, never a generic gallery.

Partial assignments are kept in memory and one sessionStorage metadata entry,
keyed by session/template/retake identity. Reloads and periodic status messages do
not clear them. Completed assignment order is persisted in existing SelectedShots.
No image bytes or filtered images are placed in draft/session metadata.

## Changed files

- `apps/bridge/Models.cs`: ASSIGN/FILTER states and logical-assignment validation.
- `apps/bridge/BoothEngine.cs`: transitions and stage guards.
- `apps/bridge/Program.cs`: three session endpoints and request record only.
- `packages/shared/src/index.ts`: additional state names.
- `apps/web/src/pages/Booth.tsx`: separate frame-first assignment and filter screens.
- `apps/web/src/boothFlow.ts`: customer phase mapping, independent counts, draft persistence.
- `apps/web/src/boothFlow.test.ts`, `tests/Bridge.Tests/AssignmentFlowTests.cs`.
- `apps/web/tests/booth.spec.ts`, `apps/web/tests/filters.spec.ts`: updated flow regressions.
- This report, refreshed browser screenshots and generated frontend build artifacts.

Camera providers, printing implementation, template editor, cloud implementation,
SignalR transport, bridge startup, PhotoComposer, PhotoFilters, FilterPicker and
browser filter processing were not changed.

## Validation

- Bridge unit tests: 73 passed.
- Web unit tests: 40 passed.
- Web production build and lint: passed (existing SignalR annotation warnings).
- Bridge Release build: passed, zero warnings/errors.
- Headless Edge browser regressions: 5 passed in 1.3 minutes, using isolated test
  data and mock camera/printing (`booth.spec.ts` and `filters.spec.ts`).

The browser regressions exercise six-step capture progress, ASSIGN before FILTER,
absence of the old gallery/reorder path, Photo 3/Photo 5 slot assignment, partial
draft reload, duplicate-strip updates, replacement, preserved slot order, filter
previews, composition, original preservation, retake and mock printing.
