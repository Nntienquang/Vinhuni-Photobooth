# Cloud sharing from the local bridge

Set these environment variables for the **bridge process**, then restart it:

```dotenv
CLOUD_SHARE_BASE_URL=https://vinhuni-photobooth-share.vercel.app
UPLOAD_API_KEY=
```

The base URL defaults to the production URL above. Supply the key securely in the bridge environment; never use a `VITE_` variable or put a real key in source, session JSON, screenshots or logs. The bridge does not automatically load `.env.example`. Older `PHOTOBOOTH_CLOUD_URL` / `PHOTOBOOTH_UPLOAD_TOKEN` variables no longer configure this queue. The existing web cloud endpoints belong to the older deployment and are not used or changed by this integration.

Confirmation starts a background upload of the existing composed final. Waiting until confirmation locks the final avoids uploading a preview that the customer may retake. Printing and completion do not await cloud work. Disabling the cloud setting leaves upload status `none`.

The bridge POSTs only `{contentType, size}` to `/api/upload` using the API key. It then streams the same file directly to Vercel Blob with the returned pathname and client token. The manual .NET request follows the [Vercel Blob SDK PUT implementation](https://github.com/vercel/storage/blob/main/packages/blob/src/put.ts) and [API transport](https://github.com/vercel/storage/blob/main/packages/blob/src/api.ts): `PUT https://vercel.com/api/blob/?pathname=...`, bearer client token, API version 12, public access and detected content type. Token options remain controlled by the authorization service. JPEG/PNG signatures and the 20 MiB limit are checked before requesting authorization. No conversion, resizing or recompression occurs.

Existing `UploadStatus` and `ShareUrl` fields are reused. `ShareId`, `ImageUrl`, `CloudLastError` and `CloudUpdatedAt` are added to the existing SQLite JSON/session JSON persistence. Tokens are never persisted. Upload saves merge only cloud metadata, preserving print state. Failures store short fixed messages or HTTP status codes, never response bodies.

THANK_YOU uses the installed `qrcode.react` library to generate a black/white, 300px SVG with a four-module quiet zone from `ShareUrl`. It shows progress or failure text when appropriate. The new-session button and existing automatic reset remain available; the existing thank-you timeout is unchanged. Operators can increase that timeout using the existing setting if customers need more scanning time.

Missing configuration, offline networking and upload failures leave the final local and do not put the session into ERROR. History Retry upload uses the same queue and existing file. Successful sessions hide retry. The existing 30-second background retry also handles failed or interrupted uploads when cloud sharing is enabled and configured. An ambiguous network failure after Blob accepts a file may leave an orphaned cloud object on retry because the supplied authorization contract has no idempotency/finalization endpoint.

Hardware/production acceptance still requires a configured bridge key, Sony A7 III, CP1300 and a phone: capture six, assign four, compose, confirm, print, finish, scan the QR, and compare the downloaded image with the local final. Repeat without Internet, then restore Internet and retry from history. Automated HTTP tests use controlled responses and do not prove production authorization or phone scanning.

Verification on 2026-10-09:

- `dotnet test tests/Bridge.Tests -c Release --no-restore`: 77 passed. Includes manual and automatic mock print flows with six captures, four assignments, compose, stalled upload, THANK_YOU, reset, failure and successful retry; final bytes and print state remain unchanged.
- Frontend Vitest: 32 passed. Production TypeScript/Vite build and ESLint passed. Build reports existing SignalR annotation warnings only.
- `BOOTH_QR_PREVIEW_URL=http://127.0.0.1:4312` with Playwright `cloud-qr.spec.ts`: passed against an isolated static preview. Checks uploading/failed/uploaded states, new-session availability, 300px QR and full QR visibility at 1920x1080, 1280x800 and 390x844.
- `git diff --check`: passed. No camera, printer, PhotoComposer, assignment or template-editor files changed. No commit or push performed.
- The older full browser regression suite was not run against the operational bridge. Its core six/four/compose/print path was exercised in isolated bridge integration tests instead. Physical Internet disconnection, production upload, phone scanning and Sony/CP1300 hardware acceptance remain outstanding. No upload key was present in process, user or machine environment. The existing automatic thank-you timeout may expire before a slow upload finishes; history retains retry/share access.

Changed files: `.env.example`; `apps/bridge/CloudQueue.cs`, `BoothEngine.cs`, `Models.cs`, `Storage.cs`, `Program.cs`; `apps/web/src/pages/Booth.tsx`, `Admin.tsx`, `apps/web/src/style.css`; `packages/shared/src/index.ts`; `tests/Bridge.Tests/StorageTests.cs`.

Added files: `tests/Bridge.Tests/CloudQueueTests.cs`, `apps/web/src/cloudQr.test.tsx`, `apps/web/tests/cloud-qr.spec.ts`, and this document. The pre-existing untracked `canonjpg/` directory was left untouched.
