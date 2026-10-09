# Local bridge API

Base: `http://127.0.0.1:4310`. JSON camelCase, enum state UPPER_CASE. POST/PUT/DELETE bắt buộc `X-Booth-Client: 1`. Không mở CORS wildcard. Chỉ nhận Host loopback và Origin local 4310 hoặc Vite 5173. API/media không được cache. Không expose bridge ra LAN.

| Method | Route | Công dụng |
|---|---|---|
| GET | `/api/system/status` | State, current session, settings, counters, devices, activity |
| GET/PUT | `/api/settings` | Load/save typed settings; chỉ idle/maintenance |
| POST | `/api/booth/start` | Reset current session, chuyển IDLE nếu camera connected |
| POST | `/api/booth/select` | IDLE → FRAME_SELECTION |
| POST | `/api/booth/pause` | Pause/cancel capture |
| POST | `/api/booth/finish` | Confirmed session → THANK_YOU |
| GET | `/api/camera/status` | Provider/model/live/error |
| POST | `/api/camera/connect`, `/disconnect` | Initialize/connect/reconnect hoặc disconnect |
| POST | `/api/camera/live-view/start`, `/stop` | EVF control |
| POST | `/api/camera/capture` | Admin test capture lúc không có phiên |
| GET/POST | `/api/templates` | List hoặc upload multipart `metadata` JSON + `file` PNG |
| PUT/DELETE | `/api/templates/{id}` | Edit/delete template |
| POST | `/api/templates/{id}/duplicate` | Clone với id mới |
| GET | `/api/templates/{id}/export` | ZIP package |
| POST | `/api/templates/import` | Multipart `file` ZIP, validate allowlist |
| POST | `/api/session` | `{templateId}` bắt đầu LIVE_VIEW |
| GET | `/api/session/current` | Current session hoặc null |
| GET | `/api/sessions` | History |
| POST | `/api/session/{id}/capture` | Accepted 202; async countdown + shots + compose |
| POST | `/api/session/{id}/compose` | Retry composition khi ERROR và đủ originals |
| POST | `/api/session/{id}/retake` | Kiểm tra retake limit, giữ take cũ |
| POST | `/api/session/{id}/confirm` | Lock final, queue cloud, optional auto print |
| POST | `/api/print/{id}` | Print/retry guarded; sent/sending/uncertain bị từ chối |
| GET | `/api/prints` | Print-job history |
| POST | `/api/print/{id}/resolve` | `{alreadyPrinted: boolean}` xác nhận thủ công kết quả uncertain sau khi kiểm tra spooler |
| GET | `/api/printers` | Installed Windows printers hoặc mock printer |
| POST | `/api/printer/test` | Compose sample và test print |
| POST | `/api/cloud/retry` | Retry pending uploads |
| POST | `/api/cloud/{id}/upload` | Upload một confirmed final |
| POST | `/api/storage/open` | Open data folder trong Explorer trên máy local |
| GET | `/media/templates/{id}` | PNG overlay |
| GET | `/media/sessions/{id}/final` | Final high resolution |
| GET | `/media/sessions/{id}/shot/{index}` | Original theo zero-based index |

SignalR hub `/hubs/booth`. Customer gọi `RegisterCustomer` mỗi connect/reconnect; hub đếm kết nối và xóa khi disconnected. UI subscribe `systemStatusChanged`, `sessionChanged`, `cloudUploadFinished` (full snapshot), `liveViewFrame` (base64 JPEG EVF), `countdownTick` (integer). Domain events bổ sung: `countdownStarted`, `photoCaptured`, `compositionStarted`, `compositionFinished`, `printingStarted`, `printingFinished`, `boothReset`.

Conflict/busy →409; invalid inputs →400; missing resource →404; unexpected failure →500 với message chung. Technical details vào local structured logs, không render stack trace cho customer.

State flow:

```text
IDLE → FRAME_SELECTION → LIVE_VIEW → READY → COUNTDOWN → CAPTURING
  ↑                                     ↑                 │
  │                                     └ BETWEEN_SHOTS ←──┘ (lặp theo frame)
  │                                                        │
  │                                               COMPOSING → FINAL_PREVIEW
  │                                                              │
  │                                               retake → LIVE_VIEW
  │                                                              │ confirm
  └ THANK_YOU ← QR_READY ← PRINTING (optional) ←──────────────────┘

Lỗi / ngắt camera → ERROR / MAINTENANCE → nhân viên reconnect/reset
```

## Runtime camera endpoints

GET `/api/camera/preferences` returns provider/autoConnect/startLiveView. PUT saves startup options for the current provider. POST `/api/camera/provider` accepts the same object and switches immediately. POST `/api/camera/scan` returns normalized devices. POST `/api/camera/reconnect` recreates the current provider. Existing connect/disconnect/live-view/capture routes remain supported; capture now returns `{ localJpegPath }`. Provider changes return 409 with `Cannot change camera while a photo session is active.` during a session, including when paused. All mutations require `X-Booth-Client: 1`.
