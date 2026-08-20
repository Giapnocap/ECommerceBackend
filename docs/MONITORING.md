# Monitoring và cảnh báo

Tài liệu này mô tả các tín hiệu quan sát hiện có của backend. Các ngưỡng bên dưới là điểm khởi đầu
cho staging; cần điều chỉnh theo lưu lượng thực tế trước khi dùng làm SLO hoặc SLA.

## Thu thập telemetry

- Serilog ghi log có cấu trúc và correlation ID cho từng HTTP request.
- OpenTelemetry thu trace ASP.NET Core, HTTP client, activity nghiệp vụ và metric runtime.
- Query string và thông tin xác thực không được đưa vào trace outbound.
- Health request không được trace để tránh nhiễu.
- OTLP chỉ được gửi khi `Observability:Otlp:Enabled=true`; endpoint lấy từ
  `Observability:Otlp:Endpoint`.
- Production nên dùng `Observability:TraceSamplingRatio` nhỏ hơn `1`; request lỗi và giao dịch
  quan trọng nên được giữ lại bằng chính sách sampling tại collector nếu nền tảng hỗ trợ.

Không có collector hoặc dashboard được đóng gói trong repository. Việc kết nối Prometheus,
Grafana, Application Insights hay một nền tảng tương đương thuộc cấu hình môi trường triển khai.

## Health endpoints

| Endpoint | Phạm vi | Quyền truy cập | Cách dùng |
|---|---|---|---|
| `GET /health/live` | Tiến trình API | Anonymous | Liveness probe; chỉ kiểm tra ứng dụng còn chạy. |
| `GET /health/ready` | Database, storage và worker bắt buộc | Anonymous, chỉ trả trạng thái tổng | Readiness probe; loại instance khỏi tải khi `Unhealthy`. |
| `GET /health` | Toàn bộ health check | Anonymous, chỉ trả trạng thái tổng | Kiểm tra vận hành tổng quát. |
| `GET /health/details` | Trạng thái, thời gian và dữ liệu từng check | Role `Admin` | Chẩn đoán có kiểm soát; không công khai ra Internet. |

Các readiness check hiện có: `database`, `product-image-storage`, `outbox`, `order-expiration`,
`payment-reconciliation` và `data-retention`. Timeout mỗi dependency được cấu hình bằng
`HealthChecks:DependencyTimeoutSeconds`.

## Metric ứng dụng

| Meter | Metric | Ý nghĩa |
|---|---|---|
| `ECommerceBackend.Business` | `commerce.operations`, `commerce.operation.duration` | Số lượng, kết quả và thời gian use case nghiệp vụ. |
| `ECommerceBackend.Database` | `database.commands`, `database.command.duration` | Số câu lệnh và độ trễ truy cập database. |
| `ECommerceBackend.Auth` | `auth.session.validations`, `auth.session.validation.duration` | Kết quả và độ trễ kiểm tra phiên JWT với database. |
| `ECommerceBackend.Catalog` | `catalog.queries`, `catalog.query.duration`, `catalog.query.result_count` | Lưu lượng, độ trễ và kích thước kết quả catalog. |
| `ECommerceBackend.Outbox` | `outbox.messages.processed`, `outbox.messages.failed`, `outbox.messages.dead_lettered` | Kết quả dispatch notification. |
| `ECommerceBackend.Outbox` | `outbox.backlog.pending`, `outbox.backlog.dead_lettered`, `outbox.backlog.oldest_age` | Backlog được quan sát bởi readiness check. |
| `ECommerceBackend.OrderExpiration` | `orders.expired`, `orders.expiration.failed` | Kết quả worker hết hạn đơn COD. |
| `ECommerceBackend.PaymentReconciliation` | `payments.reconciliation.examined`, `payments.reconciliation.updated`, `payments.reconciliation.failed` | Kết quả đối soát payment. |
| `ECommerceBackend.Operations` | `data_retention.runs`, `data_retention.records.changed`, `data_retention.lock_contentions`, `data_retention.duration` | Vận hành data retention. |
| `ECommerceBackend.Operations` | `uploads.orphans.deleted`, `outbox.dead_letters.redriven`, `audit.events.enqueued` | Dọn upload, redrive và audit. |

OpenTelemetry còn phát metric chuẩn của ASP.NET Core, `HttpClient` và .NET runtime. Tên metric cụ
thể phụ thuộc phiên bản instrumentation; dashboard nên khám phá từ collector thay vì hard-code tên
không thuộc contract của ứng dụng.

## Baseline cảnh báo

| Tín hiệu | Điều kiện cảnh báo khởi điểm | Mức xử lý |
|---|---|---|
| Liveness | `/health/live` thất bại 2 lần liên tiếp | Critical; restart instance và kiểm tra startup log. |
| Readiness | `/health/ready` thất bại trên 2 phút | High; xem `/health/details`, database và worker heartbeat. |
| HTTP 5xx | Tỷ lệ trên 2% trong 5 phút và tối thiểu 20 request | High; nhóm theo route, exception code và correlation ID. |
| Database latency | p95 `database.command.duration` trên 500 ms trong 10 phút | High; kiểm tra blocking, plan và resource SQL Server. |
| Outbox age | `outbox.backlog.oldest_age` trên `Outbox:MaxPendingAgeMinutes` | High; kiểm tra SMTP, lease và retry. |
| Dead letter | `outbox.backlog.dead_lettered` lớn hơn 0 | Medium; xác định nguyên nhân trước khi redrive. |
| Payment reconciliation | `payments.reconciliation.failed` tăng hoặc health check degraded/unhealthy | High; kiểm tra provider, credential và record stale. |
| Order expiration | `orders.expiration.failed` tăng hoặc worker heartbeat stale | High; bảo vệ tồn kho đang giữ và kiểm tra database. |
| Data retention | Health check degraded/unhealthy hoặc lock contention tăng liên tục | Medium; không chạy song song nhiều job và kiểm tra batch. |
| Auth anomaly | Tỷ lệ outcome `inactive`/`invalid_claims` tăng đột biến | Medium; kiểm tra token reuse, clock và chiến dịch tấn công. |
| Webhook rejection | Log/audit `invalid signature`, `replay`, `currency mismatch` tăng đột biến | High; không retry thủ công trước khi xác định nguồn. |
| Storage/disk | Dung lượng volume upload hoặc SQL còn dưới 20% | High; tín hiệu do nền tảng host cung cấp. |
| Backup | Backup job thất bại hoặc không có bản backup mới theo RPO vận hành | Critical; tín hiệu do scheduler/backup platform cung cấp. |
| TLS/certificate | Chứng chỉ còn dưới 14 ngày hoặc probe HTTPS thất bại | High; tín hiệu do ingress/monitor bên ngoài cung cấp. |

## Chẩn đoán sự cố

1. Ghi lại thời điểm, environment, endpoint và correlation ID từ response/header.
2. Kiểm tra `/health/live`, sau đó `/health/ready`; Admin dùng `/health/details` để xác định dependency.
3. Tra log theo correlation ID và stable error code trong `ProblemDetails`.
4. Mở trace tương ứng để phân biệt lỗi API, SQL Server và external provider.
5. Đối chiếu metric trước và sau thời điểm lỗi; không redrive outbox, webhook hoặc refund khi chưa
   kiểm tra idempotency key/provider reference.
6. Ghi lại thao tác phục hồi và xác nhận invariant dữ liệu sau sự cố.

Việc thử cảnh báo thật trên staging, dashboard của collector, backup scheduler, dung lượng volume và
TLS probe cần hạ tầng bên ngoài nên không thể được xác nhận chỉ bằng source code hoặc test local.
