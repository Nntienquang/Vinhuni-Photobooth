# Canon SDK (không được phân phối trong repository)

Tải Canon EDSDK Windows x64 từ cổng developer chính thức, chấp nhận điều khoản của Canon. Chọn bản có hỗ trợ **EOS R50 V / R50V**; đối chiếu release notes và compatibility list đi kèm SDK, không suy luận từ EOS R50.

Đặt `EDSDK.dll`, `EdsImage.dll` và tất cả runtime dependencies mà gói SDK yêu cầu vào thư mục này. Build/publish sao chép DLL cạnh executable. Không chép DLL x86. Không commit các binary này. Nếu Windows báo thiếu Visual C++ runtime, cài bản runtime mà Canon chỉ định.

`CAMERA_PROVIDER=canon` dùng P/Invoke trên một STA worker riêng. `CAMERA_PROVIDER=mock` không tải SDK và chạy độc lập. Wrapper cần được đối chiếu với `EDSDK.h`, `EDSDKTypes.h` trong bản SDK được cấp và kiểm thử trực tiếp với camera trước sự kiện.

Xem [CANON_SETUP](../../../../docs/CANON_SETUP.md).
