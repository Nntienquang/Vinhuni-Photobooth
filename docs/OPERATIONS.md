# Vận hành và phục hồi

## Bắt đầu ca

Build trước ngày chạy. Mở bridge local, Admin trên laptop, Booth trên HDMI Extend. Test camera và máy in thật; kiểm tra `mock`/`canon` và `mock`/`windows` trên dashboard để không vận hành nhầm mode. START / RESET xóa current session khỏi màn hình, không xóa ảnh đã lưu. Chỉ reset khi nhân viên đã xác nhận khách hiện tại kết thúc hoặc cần hủy phiên.

Countdown và capture do bridge thực hiện. Khách không thể chụp trước khi countdown kết thúc bằng double click ở UI. Capture/compose khóa thao tác; không thay cấu hình khi phiên đang chạy. Frame và overlay được snapshot khi bắt đầu phiên.

## Pause / lỗi camera

PAUSE hủy countdown/capture đang chờ. Khi camera ngắt, customer chỉ nhận thông báo hỗ trợ; Admin/log giữ chi tiết. Phiên bị gián đoạn không tự tiếp tục để tránh chụp khách lúc chưa sẵn sàng. Reconnect rồi reset; ảnh đã chụp vẫn còn trên disk. Nếu đã đủ shots nhưng compose lỗi, Admin có Retry composition.

Mất kết nối màn hình khách chỉ làm badge giảm; bridge vẫn giữ công việc đang chạy. Mở lại `/booth` đồng bộ trạng thái hiện tại. Không cố capture thêm bằng nhiều cửa sổ. SignalR tự reconnect với backoff; bridge vẫn là nguồn trạng thái duy nhất.

## Print recovery

`none` → `sending` → `sent` hoặc `failed`. Lệnh in dùng final đã confirm. `sent` không thể tự in lại bằng double click. Khi bridge khởi động lại và thấy `sending`, phiên chuyển `uncertain`: Windows có thể đã nhận job.

Kiểm tra Windows spooler và bản giấy trước khi gửi lại. Với `uncertain`, History có nút **Đã in** hoặc **Chưa in · cho phép retry**; thao tác yêu cầu nhân viên xác nhận đã kiểm tra và được ghi log. Không sửa database để cưỡng ép in. Khi lỗi driver, không xóa originals/final. Nếu cần bản in bổ sung của phiên `sent`, mở final high resolution từ History và dùng thao tác in thủ công được nhân viên kiểm soát; flow tự động không có nút in lặp vô hạn.

## Cloud recovery

`pending` lưu trong SQLite ngay khi xác nhận final. Cloud retry chạy độc lập với gate chụp/in. Mỗi HTTP attempt có timeout 12 giây, retry định kỳ 30 giây hoặc bấm RETRY. Khi booth reset hay restart, hàng đợi vẫn còn. Upload chỉ cập nhật metadata cloud của bản ghi mới nhất, không ghi đè trạng thái in.

Không có token/domain thì không hiện QR giả. Final lớn hơn 4 MB không được gửi qua API Vercel hiện tại; vẫn lưu/in được local. Kiểm tra queue trước khi khách rời đi nếu sự kiện hứa giao ảnh online ngay.

## Storage / mất điện

Luôn dùng thư mục local writable và ổ đủ trống. File ghi temporary rồi rename, nhưng atomic filesystem và SQLite không thể là một transaction chung: nếu mất điện đúng khoảng giữa hai bước, đối chiếu `session.json` và `booth.db` trước khi khôi phục thủ công.

Originals và take cũ được giữ. Không tự động dọn data. Backup khi booth đã PAUSE và dừng process; sao chép toàn bộ `data` gồm SQLite cùng các file WAL/SHM nếu còn. Thử backup/restore trên bản sao trước ngày sự kiện. Không chép riêng `booth.db` khi bridge đang ghi WAL.

Session chưa confirm sau restart được đánh dấu ERROR trong History, không tự chụp tiếp. Phiên mới bắt đầu an toàn ở IDLE nếu camera kết nối. Final đã confirm vẫn xem/tải được từ History.

## Giới hạn đã biết / kiểm tra hardware bắt buộc

- Native EDSDK adapter chưa được chạy với SDK vendor và EOS R50 V tại đây; cần đối chiếu ABI với đúng SDK headers và test trực tiếp.
- Windows spooler không chứng minh bản giấy đã ra. Driver có thể trì hoãn/offline mà chưa báo lỗi; kiểm tra vật lý và queue Windows.
- Fullscreen/multi-display/wake lock phụ thuộc browser, user gesture và OS policy. Không tự động tắt Windows sleep hoặc khóa hệ điều hành.
- Không có Admin PIN trong bản này; dùng Windows account/physical access cho nhân viên. Local API giới hạn loopback, Origin/Host và custom header để chống website lạ gửi lệnh.
- Không có auto-delete ảnh, RAW archive hoặc remote admin. Originals/finals luôn giữ để ưu tiên phục hồi.
- Preview template dùng placeholder ô ảnh; compositor thật được kiểm tra bằng mock capture. Kiểm tra frame thật bằng phiên test trước sự kiện.
- Cloud credentials/Blob store chưa được provision ở môi trường này. Signed URL/QR online cần smoke test sau deploy.

Đây là source có workflow mock chạy thật và adapters phần cứng, chưa phải chứng nhận thiết bị đã sẵn sàng cho một ca đông người. Chạy soak test trên đúng laptop/camera/driver ít nhất bằng tải dự kiến của sự kiện.
