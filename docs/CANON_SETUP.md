# Canon EOS R50 V / EOS RP — Windows x64

1. Xin quyền truy cập Canon EDSDK theo khu vực tại [Canon Developer Resources](https://asia.canon/en/campaign/developerresources/camera/cap/edsdk-eos-digital-camera-sdk-release-note) hoặc [Canon USA SDK](https://www.usa.canon.com/support/sdk). Release notes của Canon có mục hỗ trợ EOS R50V; kiểm tra bản SDK được tải, firmware, license và compatibility matrix đi kèm.
2. Không bundle SDK vào public source. Đặt native x64 DLL (`EDSDK.dll`, `EdsImage.dll` và dependencies theo gói được cấp) tại `apps/bridge/vendor/canon/`, rồi `pnpm build`. Project chép DLL cạnh executable. Cài runtime Visual C++ theo Canon nếu cần.
3. USB-C cable phải truyền dữ liệu. Dùng cổng trực tiếp, hạn chế hub. Cấp nguồn ổn định. Thiết lập still-photo/JPEG; RAW+JPEG chỉ JPEG được đưa vào flow (RAW không lưu mặc định).
4. Đóng EOS Utility, Teams, OBS và mọi phần mềm đang giữ camera. Không mở EOS Utility đồng thời với bridge.
5. Chạy `./scripts/start-booth.ps1 -Camera canon -Printer mock`. Mở Admin. TEST CAMERA / RECONNECT phải hiển thị model thật và Connected. TEST CAPTURE tạo JPEG tại `data/originals`; kiểm tra bằng viewer độc lập.
6. Kiểm tra EVF preview và full shot khác nhau: final lấy object download từ EDSDK, không lấy EVF. Kiểm tra portrait EXIF, crop và mirror.

## Wrapper

`Camera/CanonEdSdkService.cs` sở hữu một worker STA, hàng đợi command và event pumping qua `EdsGetEvent`. Tất cả native calls dùng cùng thread. Callback delegates giữ strong reference; SDK references được release. SaveTo=Host, capacity, EVF output PC, TakePicture, object transfer, JPEG download và session cleanup được tách khỏi UI.

Capture timeout 25 giây đóng session để late object event không bị gán cho shot tiếp theo. RAW transfer được cancel để ưu tiên JPEG. Khi camera ngắt, capture bị hủy và booth về trạng thái hỗ trợ; originals đã tải không bị xóa. Reconnect do nhân viên bấm, tránh tự tiếp tục chụp khi khách chưa sẵn sàng.

## Reconnect

PAUSE → kiểm tra nguồn/cáp → đóng app chiếm camera → TEST CAMERA / RECONNECT → xác nhận preview → TEST CAPTURE khi booth không có phiên → START / RESET.

Nếu thiếu DLL hoặc sai kiến trúc, Admin báo lỗi thật, customer ở Maintenance. Dùng `CAMERA_PROVIDER=mock` để phát triển độc lập. Không tự fallback từ canon sang mock.

## Phạm vi kiểm chứng

Native wrapper đã compile nhưng chưa được chạy với SDK/camera vật lý trong môi trường này. Trước sự kiện bắt buộc đối chiếu struct/signature/constants với headers của SDK được cấp; test EVF, ảnh JPEG/RAW+JPEG, focus, disconnect, timeout, reconnect và nhiều phiên liên tục. Không dùng kết quả mock để tuyên bố Canon đã được chứng nhận hoạt động.

Runtime selection: choose Canon EDSDK in Admin > Settings > Camera & capture. Saved camera preferences take precedence over CAMERA_PROVIDER and start-booth.ps1 -Camera. Both EOS R50 V and EOS RP are recognized; validate each physical model with your licensed SDK version.
