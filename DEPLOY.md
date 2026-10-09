# Vercel cloud deployment

Cloud không chạy USB/camera/printer. Booth chính luôn mở tại `http://127.0.0.1:4310`. Deploy này cung cấp `/p/:shareId` và API upload/download; Admin trên Vercel không được phép điều khiển bridge loopback vì origin bảo mật.

## Trạng thái

Không tìm thấy Vercel CLI authentication hoặc `VERCEL_TOKEN` khi triển khai source. Chưa có production URL; không có cloud smoke test thật.

## Setup

```powershell
pnpm install --frozen-lockfile
pnpm lint
pnpm test
pnpm build
pnpm dlx vercel login
pnpm dlx vercel link
```

Trong Vercel Project Settings: **Root Directory = apps/web**, bật quyền đọc files ngoài root để resolve workspace `packages/shared`. Framework Vite; build command `pnpm --filter @booth/web build`; output `dist`. Cấu hình `apps/web/vercel.json` giữ API routes trước SPA fallback. Chạy CLI từ repository root đã link project; không upload `data`, SDK DLL hoặc token.

Tạo **Private Blob store** trong Storage, connect với project. Đặt environment cho Preview và Production:

| Biến | Nơi dùng |
|---|---|
| `BLOB_READ_WRITE_TOKEN` | Vercel server only, token của private Blob store |
| `PHOTOBOOTH_UPLOAD_TOKEN` | Vercel và Windows bridge, cùng giá trị secret ngẫu nhiên dài |
| `PHOTOBOOTH_PUBLIC_URL` | Vercel server, HTTPS domain sẽ hiển thị trong QR |
| `PHOTOBOOTH_CLOUD_URL` | Windows bridge, cùng HTTPS domain |
| `VITE_CLOUD_BASE_URL` | Dành cho mở rộng UI; booth hiện tại dùng cloud URL phía bridge, không cần biến này |

Có thể dùng `pnpm dlx vercel env add NAME preview` và `pnpm dlx vercel env add NAME production`; nhập token qua prompt, không ghi vào source. Không đặt secret trong `VITE_*`.

```powershell
pnpm dlx vercel              # preview
# Kiểm tra preview API với token, Blob private, trang /p và QR điện thoại
pnpm dlx vercel --prod       # production sau khi smoke test đạt
```

Preview có Deployment Protection sẽ chặn bridge/khách nếu chưa cấu hình quyền truy cập phù hợp. Production `/p/:shareId` phải truy cập được không login; `/api/upload` luôn cần booth token.

## API

`POST /api/upload`: `Authorization: Bearer <secret>`; multipart fields `file`, `shareId` UUID. JPEG/PNG ≤4,000,000 bytes. Giới hạn thấp hơn payload limit của Vercel Functions, tính cả multipart overhead. Final quá lớn vẫn được lưu/in local và nằm pending; không hạ chất lượng file in để né giới hạn cloud.

Private path `photobooth/<UUID>.jpg` hoặc `.png`. Không chứa tên khách. Retry cùng UUID dùng overwrite để idempotent. Kết quả `{shareId,shareUrl}`. Không upload originals.

`GET /api/photo/<UUID>?signed=1`: cấp signed read URL scoped đúng object, hạn 5 phút bằng `issueSignedToken`/`presignUrl`. Không trả signing material hoặc read-write token. `/p/<UUID>` render ảnh từ URL này. `GET /api/photo/<UUID>?download=1` stream file private từ server kèm Content-Disposition. Share UUID là capability link; ai có link đều tải được, không phải tài khoản riêng.

Tham khảo [Vercel Blob SDK](https://vercel.com/docs/vercel-blob/using-blob-sdk) và [private storage](https://vercel.com/docs/vercel-blob/private-storage). Không bật public storage cho tiện rồi bỏ qua kiểm thử.

## Smoke test trước production

- Upload không token → 401; token sai → 401; file không phải ảnh/UUID sai → 400.
- Upload final JPEG/PNG → 200; same UUID retry không tạo nhiều ảnh.
- Blob không đọc được bằng URL private trần; signed URL đọc được và hết hạn.
- Trang /p hoạt động trên điện thoại; Download giữ đúng resolution.
- QR trỏ HTTPS production domain, không localhost hay preview domain.
- Ngắt Internet booth vẫn chụp/in; reconnect mạng rồi Retry Cloud Uploads → pending giảm.
- Lập lịch quản lý retention/chi phí Blob theo sự kiện, thông báo người tham gia thời gian lưu ảnh.
