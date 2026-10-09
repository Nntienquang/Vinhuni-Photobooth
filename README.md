# VINHUNI PHOTOBOOTH

Hybrid photobooth cho Windows 11 x64: React UI + ASP.NET Core local bridge + Canon EDSDK adapter + SkiaSharp + Windows printing + optional Vercel private Blob.

**Mock workflow đã chạy qua E2E. Camera Canon, máy in thật và Vercel cần kiểm thử với SDK/phần cứng/tài khoản thực tế; repository không chứa SDK Canon hoặc secrets.** Mock mode dùng ảnh studio tạo bằng code, hiển thị rõ MOCK; không giả webcam thành camera Canon.

## Architecture

```text
Admin /admin/dashboard ─┐
                       ├─ HTTP + SignalR ─ Local bridge 127.0.0.1:4310
Customer /booth ────────┘                    ├─ Canon EDSDK / Mock provider
                                            ├─ SkiaSharp compositor
                                            ├─ SQLite + session files
                                            ├─ Windows spooler / Mock printer
                                            └─ durable upload queue → Vercel API → private Blob
                                                                            └─ /p/:shareId
```

- `apps/web`: React 19, TypeScript strict, Vite, Tailwind 4, React Router, SignalR, PWA, cloud functions.
- `apps/bridge`: .NET 8 Windows x64; điều phối state, countdown, capture, composition, print và local persistence.
- `packages/shared`: các TypeScript contracts dùng cho UI.
- `tests/Bridge.Tests`: validation, crop, composition, mock camera, state guards, retake, duplicate print, persistence/import tests.
- `data`: sinh tự động khi bridge chạy. Không commit ảnh khách vào source control.
- `docs`: thiết lập thiết bị, vận hành, API, triển khai và giới hạn đã biết.

Bridge là nguồn trạng thái duy nhất. Hai browser không tự countdown/capture riêng. Command được serialize bằng semaphore, transition được kiểm tra ở server. Cloud retry không giữ khóa booth trong lúc đợi Internet. Originals dùng full-resolution camera JPEG, EVF chỉ hiển thị preview. Frame được snapshot cùng phiên để việc sửa template không thay đổi phiên đang chụp.

## Requirements

- Windows 11 64-bit; .NET SDK 8 cho development (đã kiểm thử với SDK 8.0.413).
- Node.js 22.12+ hoặc 24 và pnpm 10. Lockfile được commit để tái lập dependencies.
- Edge/Chrome mới. Playwright config mặc định dùng Edge đã cài trên Windows.
- Canon EDSDK Windows x64 được cấp phép riêng nếu sử dụng camera thật.
- Driver Windows của máy in nếu sử dụng in thật.

## Development

```powershell
corepack enable
corepack prepare pnpm@10.18.3 --activate
pnpm install
pnpm dev
```

Nếu Node không kèm Corepack: `npm install --global pnpm@10.18.3`.

Development UI: `http://127.0.0.1:5173/admin/dashboard`. Vite proxy `/api`, `/media`, `/hubs` về bridge 4310. Bridge có thể chạy riêng:

```powershell
cd apps/bridge
dotnet restore
dotnet build
dotnet run
```

Trong workspace đã chuẩn bị, pnpm cục bộ cũng dùng được:

```powershell
.\.tools\node_modules\.bin\pnpm.cmd build
.\.tools\node_modules\.bin\pnpm.cmd test
.\.tools\node_modules\.bin\pnpm.cmd lint
```

## Production booth / offline mode

```powershell
pnpm build
.\scripts\start-booth.ps1 -Camera mock -Printer mock
```

Mở `http://127.0.0.1:4310/admin/dashboard`, bấm OPEN CUSTOMER DISPLAY. `pnpm build` build frontend, copy vào `apps/bridge/wwwroot`, rồi build bridge Release. Không cần chạy Vite hoặc truy cập Vercel ngày sự kiện.

Đóng bridge trước khi rebuild/deploy local. Không refresh ứng dụng đang chụp để cập nhật service worker. PWA là lớp hỗ trợ; toàn bộ HTML/CSS/JS, API, template, ảnh, countdown, capture và print được phục vụ tại máy. Để đóng gói máy khác: `./scripts/publish-bridge.ps1`, đặt `PHOTOBOOTH_DATA_DIR` tuyệt đối rồi chạy executable trong `artifacts/bridge`.

Các biến môi trường không tự được nạp từ `.env`; dùng PowerShell `$env:NAME='value'` hoặc cấu hình Windows Environment Variables trước khi chạy. `.env.example` chỉ là tài liệu; không đưa booth upload token vào biến bắt đầu bằng `VITE_`.

## Mock mode

Mặc định `CAMERA_PROVIDER=mock`, `PRINT_PROVIDER=mock`. Camera mock tạo live view 800×533 và ảnh capture riêng 2400×1600. Mock printer không gửi lệnh tới máy in Windows. Quy trình customer dùng chính compositor, file storage, database và state machine production.

## Canon EDSDK setup / Camera USB setup

Xem [docs/CANON_SETUP.md](docs/CANON_SETUP.md). Đặt SDK DLL tại `apps/bridge/vendor/canon`, rebuild, chạy `start-booth.ps1 -Camera canon`. Đóng EOS Utility và các app chiếm camera. Dùng USB-C **data cable**, nguồn điện ổn định và chế độ chụp ảnh. Không có DLL thì provider báo lỗi và booth ở Maintenance; không âm thầm thay bằng mock.

## Two display setup

1. Windows Settings → System → Display → Extend these displays.
2. Màn laptop mở `/admin/dashboard`. Bấm OPEN CUSTOMER DISPLAY.
3. Cho phép popup; nếu browser hỏi quyền Window Management, cho phép để tự chuyển popup sang màn không phải primary.
4. Nếu browser không hỗ trợ/quyền bị từ chối, kéo popup sang Display 2, bấm nút toàn màn hình trên booth hoặc F11. Browser yêu cầu user gesture cho fullscreen.
5. Kiểm tra Display badge; bật EVENT MODE tại Settings khi booth idle. Wake lock best effort; vẫn kiểm tra Windows power settings.
6. Ctrl + Shift + Q ở booth đưa về Maintenance và thoát fullscreen DOM. F11 kiosk của browser cần F11 để thoát.

## Frame creation

`/admin/templates`: upload PNG **đúng kích thước output**; vùng ảnh cần alpha trong suốt. Classic 4 Shot, Double Portrait và Single Hero tự tạo ở lần chạy đầu.

- Mỗi slot dùng tọa độ output pixel; kéo, resize hoặc nhập số. `shotIndex` chọn ảnh nguồn (zero-based trong JSON).
- Một ảnh có thể dùng lại ở nhiều slot; mọi shot phải xuất hiện ít nhất một lần. Hỗ trợ 1–4 shots và tối đa 16 slots.
- Rotation quanh tâm slot, bo góc, focal point 0–1, center-cover crop mặc định.
- PNG overlay luôn nằm trên photos. Mirror Preview mặc định ON; Mirror Final OFF và không liên quan nhau.
- Edit, Duplicate, Disable, Delete, Reorder và ZIP import/export có sẵn. ZIP chỉ nhận `template.json`, `frame.png`, `thumbnail.jpg`; không extract đường dẫn từ ZIP ra filesystem.
- Preset đầu ra: 1200×1800 @300 DPI (4×6), 1800×1200 (6×4), 600×1800 (2×6). Có thể upload kích thước custom.
- Branding: thay assets trong `apps/web/public/branding`; tên sự kiện và màu nhấn chỉnh trong Settings.

## Printer setup

Xem [docs/PRINT_SETUP.md](docs/PRINT_SETUP.md). Chạy `start-booth.ps1 -Camera canon -Printer windows`, chọn máy in đã cài, chọn kích thước giấy, lề, scaling và TEST PRINT. Print dùng final resolution đầy đủ. `sent` nghĩa là Windows spooler đã nhận, không phải giấy chắc chắn đã ra.

Auto print OFF mặc định. Customer xác nhận rồi bấm IN ẢNH. Khi Auto print ON, confirmation gửi lệnh in. Double click / lệnh lặp bị chặn ở bridge. Khi print lỗi, final/originals vẫn giữ nguyên. Sau crash trong lúc gửi in, trạng thái `uncertain` ngăn tự gửi trùng; xử lý theo [recovery](docs/OPERATIONS.md).

## Local storage

```text
data/booth.db
data/templates/<id>/{template.json,frame.png}
data/sessions/yyyy-MM-dd/<uuid>/
  template.json
  frame.png
  originals/take_0/shot_01.jpg
  originals/take_1/shot_01.jpg
  final/take_0/final.jpg
  final/take_1/final.jpg
  session.json
```

Retake giữ take cũ để khôi phục, không ghi đè ảnh gốc. File ghi qua temporary file + atomic rename. SQLite dùng WAL, tự tạo schema. Settings và print history lưu SQLite. Logs JSON lines tại working-directory `logs/booth-yyyyMMdd.log`, giữ 30 ngày. Mặc định giữ mọi original và final; không có auto-delete ảnh khách.

## Cloud upload / Vercel deployment

Xem [DEPLOY.md](DEPLOY.md). Bridge giữ `PHOTOBOOTH_UPLOAD_TOKEN` trong environment. Vercel API kiểm tra bearer token, UUID, magic bytes JPEG/PNG và giới hạn 4 MB. Chỉ upload final đã xác nhận, không upload originals. QR chứa HTTPS `/p/<UUID>`; private photo page xin signed read URL 5 phút, download qua server. Queue sống qua restart và retry mỗi 30 giây khi cấu hình cloud hợp lệ. Timeout mỗi attempt 12 giây; capture/print không phụ thuộc upload.

Không có Vercel credentials trong môi trường thực hiện nên chưa deploy. Việc scan QR online phải được xác nhận sau khi provision Blob và deploy.

## Testing

```powershell
pnpm lint
pnpm test
pnpm build
# Terminal khác: start bridge mock sau build
.\scripts\start-booth.ps1
# Terminal kiểm thử:
pnpm test:e2e
```

E2E dùng camera/printer mock, mở hai trang, chụp 4 ảnh, đảm bảo chưa capture trước countdown, từ chối capture trùng, retake, final 1200×1800, confirm, print, từ chối print trùng, pending cloud, auto reset và history. Không chạy E2E trong lúc phục vụ khách hoặc khi bridge đang dùng phần cứng thật. Xem [VALIDATION.md](docs/VALIDATION.md).

## Troubleshooting

| Triệu chứng | Xử lý |
|---|---|
| UI báo chưa kết nối bridge | Mở địa chỉ local 4310; kiểm tra bridge còn chạy và port chưa bị chiếm |
| Canon không thấy camera | Kiểm tra data cable, nguồn, still mode, SDK version, đóng EOS Utility, TEST CAMERA / RECONNECT |
| Customer Maintenance | Kiểm tra lỗi camera/admin, reconnect, rồi START / RESET |
| Capture timeout | Không bấm chụp liên tục; kiểm tra AF, chế độ máy và USB; reconnect |
| Final không đúng frame | PNG đúng output dimensions, slot trong canvas, overlay alpha, kiểm tra mirror final |
| Print bị cắt | Kiểm tra paper size/driver borderless/margin/cover vs contain; test bằng thước |
| Pending uploads | Kiểm tra token/HTTPS domain/network; final >4 MB giữ local và cần cấu hình upload phù hợp |
| Disk full | Dừng booth, sao lưu sang ổ khác, giải phóng dung lượng, reset sau khi kiểm tra ảnh đã ghi |
| pnpm không tìm thấy | Cài pnpm hoặc dùng đường dẫn pnpm cục bộ nêu trên |

## Event-day checklist

- [ ] Canon connected; đúng EDSDK hỗ trợ R50 V
- [ ] USB data cable OK; battery/continuous power OK; EOS Utility closed
- [ ] TEST CAPTURE OK, JPEG đúng resolution/orientation, AF ổn định
- [ ] HDMI detected; Extend Display; customer fullscreen
- [ ] Windows sleep tắt trong sự kiện; EVENT MODE đã bật
- [ ] Printer driver OK; đúng loại/khổ giấy; TEST PRINT OK
- [ ] Templates loaded; thử 1/2/3/4 shots khi sử dụng các frame tương ứng
- [ ] Disk space OK; thư mục data writable; backup đã chuẩn bị
- [ ] Optional cloud upload OK; QR scan/download từ điện thoại OK
- [ ] Offline test hoàn chỉnh (chụp, ghép, lưu, in, reset)
- [ ] Rút/cắm USB và reconnect test
- [ ] Restart/recovery test; kiểm tra uncertain print trong spooler
- [ ] Chạy nhiều phiên liên tiếp trên đúng hardware trước khi mở gian hàng

## Runtime camera selection

Select Auto Detect, Canon EDSDK, Sony, or Mock in Admin > Settings > Camera & capture. Provider changes and startup preferences are saved locally without restarting the bridge. See [runtime camera operation](docs/RUNTIME_CAMERAS.md) and [Sony dependency status](docs/SONY_SETUP.md). Canon supports EOS R50 V and EOS RP; Sony ILCE-7M3 currently reports DependencyMissing until its USB/PTP transport is integrated.
