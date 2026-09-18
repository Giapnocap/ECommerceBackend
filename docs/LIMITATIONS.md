# Giới hạn hiện tại

Mình làm dự án này trong phạm vi một backend thương mại điện tử để học và làm portfolio, nên hiện
tại hệ thống chạy với một API instance, một SQL Server và volume local để lưu ảnh. Rate limiter và
FX cache cũng nằm trong process. Với cách chạy local này chúng đủ dùng; mình chưa thêm Redis, object
storage hay triển khai nhiều replica vì sẽ làm dự án phức tạp hơn trong khi chưa có tải thực tế để
chứng minh là cần.

Phần thanh toán đã có COD, Stripe PaymentIntent, Webhook, reconciliation và refund. CurrencyAPI và
SMTP cũng đã có adapter. Các luồng này có test tự động bằng adapter giả lập, còn phần cần kiểm tra
Transaction và lock có test với SQL Server. Mình chưa có môi trường staging cùng credential thật để
chạy trọn vẹn với Stripe Test Mode, CurrencyAPI và một hộp thư thật. Refund đang chờ ở provider cũng
chưa có worker đối soát riêng; thao tác retry hiện đi qua refund API với cùng Idempotency key.

Một số bài toán thương mại điện tử lớn hơn như nhiều kho, biến thể sản phẩm, ghép nhiều promotion,
thuế theo khu vực, tạo nhãn vận chuyển và đồng bộ tracking trực tiếp với hãng vận chuyển chưa nằm
trong phạm vi mình chọn. Email verification đã có nhưng chưa bắt buộc trước khi đăng nhập. Mình ưu
tiên hoàn thiện checkout, tồn kho, đơn hàng, phân quyền và tính nhất quán dữ liệu trước vì đây là các
phần mình muốn học kỹ nhất.

Các số liệu trong [PERFORMANCE.md](PERFORMANCE.md) là kết quả để mình phát hiện regression trên máy
local với dataset cố định. Khi có môi trường triển khai thật, mình sẽ cần đo lại qua network, cấu
hình alert, kiểm tra backup/restore định kỳ và chạy lại migration trên dữ liệu gần với thực tế hơn.
Nếu mở rộng nhiều API instance, những việc đầu tiên mình sẽ xem xét là shared/object storage,
distributed rate limiting, distributed cache và cách điều phối background worker.
