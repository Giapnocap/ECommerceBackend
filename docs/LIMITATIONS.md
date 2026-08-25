# Ranh giới và giới hạn hệ thống

Đây là backend release phù hợp cho portfolio, không phải tuyên bố hệ thống có khả năng mở rộng vô
hạn trong production. Các ranh giới sau là chủ ý và được thể hiện trong thiết kế.

## Phạm vi hiện tại

- Checkout hỗ trợ COD và Stripe PaymentIntent. Stripe bị tắt mặc định; deterministic adapter trong
  repository không thay thế một lần xác minh Stripe Test Mode thật bằng credential hợp lệ và
  Webhook endpoint có thể truy cập.
- VND là reporting base currency; VND, USD và EUR là display/payment currency được hỗ trợ. Đổi base
  currency cho dữ liệu hiện có cần migration và backfill có kiểm soát.
- Payment reconciliation phục hồi active payment bị stale. Provider-pending refund được retry
  idempotent qua refund API, nhưng chưa có refund reconciliation worker riêng.
- Refund được tạo trực tiếp trong provider dashboard được chấp nhận qua verified Webhook, nhưng
  partial external refund không có đủ local allocation data để báo cáo chính xác theo kỳ. Luồng vận
  hành được hỗ trợ là khởi tạo refund qua API này.
- Checkout hỗ trợ shipping/tax rule cấu hình được và promotion code có giới hạn. Hệ thống không tính
  live rate theo carrier, không stack nhiều promotion và không mô hình hóa jurisdictional tax.
- Shipment record và return processing là workflow nội bộ. Carrier label creation, live tracking
  synchronization, product variant và multi-warehouse inventory nằm ngoài domain hiện tại.
- Email verification và password reset dùng token hash có thời hạn, dùng một lần, được gửi qua
  Transactional Outbox. Email verification được ghi nhận nhưng không bắt buộc để sign-in.

## Ranh giới triển khai

- Topology được hỗ trợ là một API instance và một SQL Server database.
- Product image dùng local disk. Horizontal API scaling cần object storage hoặc shared durable
  volume. Readiness xác minh process hiện tại có thể tạo, flush và xóa probe file; nó không chứng
  minh shared durability, backup coverage hoặc future disk capacity.
- Rate limiting chạy trong process. Nhiều API replica cần distributed limiter.
- Session validation đọc SQL Server trên protected request. Load test hiện tại chưa chứng minh nhu
  cầu thêm Redis.
- SMTP delivery là at-least-once. Crash sau khi SMTP nhận message nhưng trước database commit có thể
  gửi trùng với cùng `Message-ID` xác định.
- FX cache chạy trong process. Nhiều API replica cần distributed cache hoặc phải chấp nhận mỗi
  instance có cache và stale fallback window riêng.

## Ranh giới vận hành

- CI configuration, packaging, migration rollback và backup/restore drill đã được hiện thực. Cloud
  deployment, DNS, certificate và managed-secret integration thật phụ thuộc môi trường mục tiêu và
  không được repository tuyên bố là đã xác minh.
- `rollback-last.sql` chỉ phù hợp khi migration cuối vẫn data-compatible. Phải phục hồi verified
  database backup nếu migration đã biến đổi hoặc xóa production data.
- Số liệu performance là regression baseline từ topology local/CI, không phải production capacity
  estimate. Index và infrastructure cần được đánh giá lại bằng production telemetry và network
  latency.

## Tương thích

- OpenAPI v1 được snapshot test. `/api/v1` là route chuẩn; `/api` là backward-compatible alias mặc
  định dùng v1.
- Thay đổi request hoặc response gây breaking change cần API version mới; DTO và error-code contract
  v1 tiếp tục ổn định.
- Tên order/payment status là public API value; đổi tên sẽ phá backward compatibility.
- Historical order detail và immutable ledger không được tái dựng từ catalog value hiện tại.
