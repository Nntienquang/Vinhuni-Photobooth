# Visual Slot Editor and photo assignment

Open **Admin → Khung ảnh → EDIT** (or **THÊM KHUNG ẢNH**).

1. Upload the transparent PNG. Its original bytes are preserved. The editor initially adopts its pixel dimensions; output dimensions can be adjusted while preserving the aspect ratio. Existing templates keep their saved geometry.
2. Select a source slot by clicking its outline or the sidebar slot list. Drag it, resize any of its eight handles, or edit X/Y/Width/Height/Rotation/Radius/Focal X/Focal Y. Coordinates are output pixels. Arrow keys move 1 px; Shift+arrow moves 10 px while the canvas has focus.
3. Use zoom, frame opacity, bounds, labels, safe margin, and edge/center snapping to align the slot to the PNG window. The PNG overlay is always locked. These display controls never alter the saved PNG or final output. Preview Template renders with full overlay opacity and no guides.
4. Photo order maps a physical slot to a selected photo index. Reorder changes drawing order, independently of photo mapping. Every selected index must appear in at least one slot before saving. Reset focal point centers the crop.
5. Save the template. Out-of-sheet rectangles and invalid focal/radius/mapping values are rejected with an error in the editor.

## Duplicate strip

Only source slots are saved. With duplication ON, generated slots are read-only and always use the source photo assignment. Copy uses `x + duplicateOffsetX`, `y + duplicateOffsetY`. Mirror reflects the **layout**, keeping photo pixels readable: `outputWidth/2 - x - width + duplicateOffsetX`, `y + duplicateOffsetY`, and negates rotation. Neither mode modifies the source slots or crops. Explicit offsets are shown beside the mode selector; old templates default to copy, half-sheet X offset, and zero Y offset.

With duplication OFF, generated slots disappear and every saved slot is independently editable. Add/duplicate source slots to build a manual second strip. No automatic repositioning or rescaling is performed when toggling duplication.

## Customer assignment

Capture count and selected count are separate. For 6 captures / 4 selections, the gallery contains all six originals; tapping an image immediately assigns it to the active logical slot. Tapping a source slot chooses the replacement target. Reusing an image moves it from its previous slot. The next empty slot is selected automatically. Compose requires four valid, distinct assignments.

Assignments stay in page memory across bridge status updates and temporary assignment-screen unmounts. Their identity includes session, template and retake number. Reset, a new session/template, or a retake starts a new assignment. A full page reload can recover submitted selections from the bridge; **unsubmitted choices are not persisted across a full reload**. Failed selection submission does not trigger composition; failed composition returns to assignment for retry.

## Rendering and limits

Editor, assignment preview and Skia composition use the same focal-centered cover crop, centered rotation, circular radius and duplicate geometry. Browser/Skia resampling and JPEG encoding can produce minor pixel-level differences. Mirror Final remains an independent existing booth setting; the assignment preview honors it. Printing and camera-provider code are unchanged by this work.

The PNG must have transparent windows where photos should show. Slots do not cut new holes in an opaque PNG. Existing size limits remain: PNG up to 20 MB; output 300–6000 px per side and at most 24 million pixels; up to 16 source slots and four selected photos. Rotation is clipped at the sheet boundary like the final output. Safe margin is a visual 60 px guide, not an output transform.

## Validation

Latest run: full `pnpm build` passed (.NET Release: 0 warnings/errors); 64 .NET tests, 26 frontend tests, and all 5 targeted Edge E2E tests passed. Browser tests also exercised a rejected selection submission and verified that no composition request followed it. Vite emitted existing SignalR annotation warnings only.

- .NET tests cover duplicate source preservation, offsets/mirror rotation, focal crop, rounded/rotated final output, PNG byte preservation and existing bridge behavior.
- Frontend tests cover geometry, rotated resize and valid assignment indices.
- `playwright test tests/booth.spec.ts tests/slot-editor.spec.ts` uses Mock camera/printing: six captures, four stable assignments through repeated status events, retake, compose, single print, reconnect; upload/drag/resize/nudge/numeric edit/copy-mirror preview/save and byte-identical PNG.
- The camera-provider E2E suite is separate; its old Sony-dependency expectations are outside this editor change. No physical camera, Windows printer or cloud service is invoked by these editor tests.

Files: `TemplateManager.tsx`, `SlotEditor.tsx`, `SlotPhoto.tsx`, `slotGeometry.ts`, `Booth.tsx`, `assignment.ts`, `style.css`, shared template types; bridge `Models.cs`, `Templates.cs`, `BoothEngine.cs`; corresponding unit/browser tests. `CameraTests.cs` uses an existing fake for the provider-selection unit test to avoid unrelated asynchronous Mock live-view teardown flakiness; no provider implementation changes.
