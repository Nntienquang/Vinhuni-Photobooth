# Validation record — 2026-10-07

Môi trường: Windows x64, .NET SDK 8.0.413, Node 24.11.1, pnpm 10.18.3, Microsoft Edge headless. Không có Canon EDSDK DLL, camera/máy in được cấp cho session, hoặc Vercel authentication.

## Đã chạy

| Kiểm tra | Kết quả |
|---|---|
| `pnpm build` | React/TypeScript/Vite/PWA và .NET Release pass |
| `pnpm lint` | ESLint pass, không disable rules để vượt kiểm tra |
| `pnpm test` | 26 .NET tests và 6 Vitest tests pass |
| `pnpm test:e2e` với bridge mock Release | 4 Playwright tests pass |
| Bridge serve `/` + API tại 4310 | HTTP 200 |
| `CAMERA_PROVIDER=canon` khi thiếu SDK | Process sống, camera connected=false, state=MAINTENANCE, lỗi thiếu SDK rõ ràng |

Unit/integration coverage: cover crop và focal clamp; 1–4 shots; validation bounds/NaN/photo mapping; state guards; retake limit; duplicate print sent/sending/uncertain; countdown settings; mock JPEG full resolution; overlay top layer + output dimensions; cả 8 EXIF orientations; offline queue qua Storage restart; cloud metadata không ghi đè trạng thái print; template ZIP roundtrip và traversal rejection; cloud auth/UUID/magic bytes/private path/response.

E2E mở Admin và Customer, kiểm tra state realtime, full 4-shot countdown, chưa có ảnh trước khi hết countdown, duplicate capture 409, retake một lần, final 1200×1800, confirm, mock print, duplicate print 409, pending upload khi không cấu hình cloud, Thank You và auto reset. Test khác xác nhận Origin/header guard, template editor, camera disconnect sau shot đầu giữ file gốc, reconnect/reset và viewport portrait. Trình duyệt chặn request ngoài loopback trong happy path; không cần CDN/font ngoài.

Ảnh QA:

- [Admin](admin-preview.png)
- [Booth](booth-preview.png)
- [Portrait booth](booth-portrait-preview.png)
- [Template editor](template-editor-preview.png)

## Các lỗi đã bắt bằng test

- EXIF orientation 7 cần đổi thứ tự rotate/mirror.
- Busboy `parts` limit phải cho phép boundary kết thúc của multipart hai trường, nếu không upload hợp lệ bị từ chối.
- Vitest phải giới hạn `src` để không tự chạy Playwright suite.
- Upload nền không giữ gate booth và không ghi đè snapshot session cũ lên trạng thái in mới.

## Chưa xác minh ngoài môi trường local

- Canon camera control thực tế: AF, native ABI đúng phiên bản SDK, EVF, TakePicture, RAW+JPEG, reconnect USB, firmware.
- Máy in/driver và giấy thực tế, borderless, màu, khổ in, physical delivery.
- HDMI physical monitor, multi-screen permission/fullscreen và wake lock với cấu hình Windows của sự kiện.
- Vercel preview/production, Blob private, signed URL expiry, QR/download trên điện thoại ngoài mạng booth.
- Soak/load test dài hạn, mất điện thật, disk-full vật lý.

Không diễn giải các bài mock/unit test là chứng nhận các hạng mục phần cứng hoặc cloud ở trên. Xem README checklist trước khi phục vụ khách.
