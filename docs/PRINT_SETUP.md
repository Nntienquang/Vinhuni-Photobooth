# Windows photo printing

1. Cài driver chính thức của máy in, xác nhận Windows Settings → Printers & scanners → Print test page thành công.
2. Đặt giấy/khay/borderless trong Printer Preferences. Không thay driver giữa sự kiện.
3. Chạy bridge với `PRINT_PROVIDER=windows`; mặc định là mock và không in giấy.
4. Admin Settings chọn đúng printer. Khổ giấy lưu theo 1/100 inch: 400×600, 600×400 hoặc 200×600. Orientation dựa trên thứ tự width/height. Templates phải khớp tỷ lệ giấy.
5. Margin=0 chỉ phù hợp nếu driver và máy hỗ trợ borderless. `contain` giữ đủ ảnh nhưng có thể có viền; `cover` phủ giấy nhưng cắt bớt ảnh nếu tỷ lệ khác.
6. TEST PRINT dùng compositor thật và sample JPEG, in full resolution. Đo kích thước/crop trên giấy thực tế. Tắt các scaling/fit-to-page ngoài ý muốn trong driver.
7. Chỉ bật Auto Print sau khi test đạt. Copies mặc định 1.

`WindowsPrintService` dùng `PrintDocument`, `StandardPrintController` và driver Windows. Không dùng browser print dialog hoặc thumbnail. In chạy trên worker để không block thread UI. Print history tại SQLite và `GET /api/prints` lưu session, printer, time, copies, status/error.

`sent` chỉ có nghĩa lệnh đã gửi vào spooler. Driver có thể tiếp tục giữ job nếu hết giấy hoặc printer offline; quan sát queue Windows. Khi app restart với trạng thái `sending`, session thành `uncertain`; không tự retry vì có thể đã in. Kiểm tra queue/giấy trước mọi thao tác in lại thủ công.

Nếu print lỗi: giữ nguyên final/originals; kiểm tra Windows queue, nguồn, giấy và kết nối; sử dụng Retry Print trong Admin khi đã xác định job cũ không còn in. Driver/máy in thật chưa được kiểm thử trong môi trường phát triển này.
