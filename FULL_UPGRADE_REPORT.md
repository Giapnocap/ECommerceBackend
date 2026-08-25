# Báo cáo các khả năng của ECommerceBackend

| Mục | Giá trị |
| --- | --- |
| Baseline audit | `b8a14616d37dd4e9a7fed541ce0a2a5fb148fad0` trên `main` |
| Ngày audit | 2026-08-20 |
| Runtime mục tiêu | .NET 8, ASP.NET Core Web API, EF Core và SQL Server |

## Mục đích

Báo cáo này mô tả các capability đã được hiện thực trong repository. Đây không phải báo cáo xác
minh vận hành và không khẳng định Stripe, CurrencyAPI, SMTP, TLS hoặc staging host đã được kiểm tra
bằng credential bên ngoài thật. Các kết quả đó được ghi riêng trong
`PRODUCTION_READINESS_REPORT.md`.

## Kiến trúc

Solution tách HTTP host, application use case, domain rule và infrastructure:

```text
ECommerceBackend (API)
    -> ECommerceBackend.Application
        -> ECommerceBackend.Domain
    -> ECommerceBackend.Infrastructure
        -> Application + Domain
```

- Controller sở hữu HTTP contract, authorization metadata và response status code.
- Application use case điều phối validation, Transaction, repository, provider port, audit và
  Outbox write.
- Domain entity và policy bảo vệ state transition, money, inventory và refund invariant.
- Infrastructure triển khai EF Core repository, SQL locking, Stripe/CurrencyAPI/SMTP adapter,
  durable local image storage và hosted worker.
- Unit test bao phủ domain rule; integration test bao phủ API và adapter đã ghép; SQL Server test
  có tag bao phủ relational constraint, locking, migration, recovery và performance.

## Các luồng nghiệp vụ

### Xác thực và phiên đăng nhập

- Registration và login dùng DTO validation cùng BCrypt password hashing.
- Login thực hiện lượng BCrypt work tương đương cho username không tồn tại, hỗ trợ lockout và trả
  unauthorized contract chung.
- Access token chứa session và token-version claim. Protected request xác minh phiên SQL-backed còn
  hoạt động.
- Refresh Token được lưu dưới dạng SHA-256 hash, xoay vòng theo family và thu hồi family khi phát
  hiện reuse.
- Password reset và email verification dùng token hash có thời hạn, dùng một lần. Password reset
  tăng token version và thu hồi phiên hiện có.

### Catalog, cart và checkout

- Catalog write bảo vệ tên category đang hoạt động không trùng, soft delete và optimistic
  concurrency.
- Cart write được serialize theo cart và lưu display snapshot; checkout luôn tính lại giá từ
  product và promotion authoritative.
- Checkout dùng Idempotency key, khóa cart và product theo thứ tự ổn định, snapshot người nhận,
  product, promotion và money, giữ stock, ghi ledger/history/Outbox rồi commit một lần.
- Retry đồng thời trả về một logical order; tái sử dụng key có xung đột trả `409`; validation thất
  bại rollback toàn bộ Transaction.

### Giao hàng, trả hàng và tồn kho

- Staff/Admin confirmation, shipment dispatch, delivery failure/retry và delivery được bảo vệ bởi
  order state transition tường minh.
- Customer chỉ được yêu cầu trả một order thuộc sở hữu, đã giao và còn trong thời hạn cấu hình.
- Staff review và receipt là hai hành động riêng. Stock chỉ được hoàn đúng một lần sau khi hàng đã
  duyệt được nhận lại.
- Mỗi stock mutation tạo một inventory transaction có số dư sau mutation.
- Low-stock threshold theo từng product hỗ trợ màn hình vận hành và báo cáo.

## Thanh toán và hoàn tiền

- COD và Stripe card chỉ được công bố khi checkout provider tương ứng đã đăng ký đầy đủ.
- `IPaymentGateway` tách Application khỏi Stripe HTTP contract.
- Tạo PaymentIntent dùng external-creation Idempotency key và lease. Network I/O chạy ngoài SQL
  Transaction, sau đó provider state được gắn trong một Transaction ngắn.
- Stripe Webhook xác minh chữ ký, timestamp, event identity, payment identity, amount và currency.
  Event trùng không lặp state transition hoặc side effect.
- Payment state machine hỗ trợ `Pending`, `RequiresAction`, `Processing`, `Paid`, `Failed`,
  `Cancelled`, `PartiallyRefunded` và `Refunded`.
- Reconciliation worker chọn batch payment active bị stale, truy vấn Stripe rồi khóa từng
  order/payment trước khi áp dụng transition hợp lệ và idempotent khi Webhook đến chậm hoặc bị lỡ.
  Topology một API hiện tại không cần distributed query lease.
- Online refund đi theo provider gốc và hỗ trợ partial/full refund. `PaymentRefund` Idempotency key,
  processing lease, Row Version và cumulative amount check bảo vệ retry và concurrency.
- COD refund là ghi nhận thủ công có audit sau khi hàng trả đã được nhận.

## Tiền tệ

- `Money` xác minh ISO code được hỗ trợ, currency scale, rounding và overflow.
- VND là reporting base currency; VND, USD và EUR là transaction currency được hỗ trợ.
- Order snapshot exchange rate, capture time và base/display total; order line snapshot base và
  display unit price.
- Refund giữ payment currency gốc và VND base amount. Refund cuối nhận phần base amount còn lại để
  tránh cumulative rounding drift.
- CurrencyAPI integration có timeout, process-local cache, single-flight fetch và stale fallback
  có giới hạn. Report tổng hợp base snapshot thay vì cộng lẫn currency.

## Độ tin cậy và vận hành

- Transactional Outbox message commit cùng business data, có retry giới hạn, lease, dead-letter và
  Admin redrive.
- SMTP dùng RFC `Message-ID` xác định; delivery được mô tả rõ là at-least-once.
- Order expiration, payment reconciliation, Outbox dispatch và data retention chạy bằng hosted
  service có health/status signal.
- Correlation ID liên kết ProblemDetails, structured Serilog request log, audit event và
  OpenTelemetry activity.
- Liveness, readiness và protected detailed-health endpoint bao phủ process, SQL Server, storage
  và worker bắt buộc.
- Docker Compose cung cấp migration ordering cùng persistent volume cho database file, upload,
  Data Protection key và log.

## Bảo mật

- JWT issuer, audience, signing key và lifetime được validate chặt khi startup.
- Permission policy và resource ownership check bảo vệ dữ liệu đặc quyền và dữ liệu riêng của
  Customer.
- API error dùng ProblemDetails contract có giới hạn và không trả stack trace ngoài Development.
- Upload kiểm tra extension, MIME type, file signature, size và generated path trước persistence.
- Security header, CORS allowlist, HSTS, HTTPS redirection và forwarded-header processing được cấu
  hình tại API boundary.
- Secret được lấy từ environment variable hoặc external secret store và không nằm trong runtime
  template đã commit.
- Audit metadata và API output che password, token, secret, API key và credential field.

## Quản trị và báo cáo

- Workflow Admin/Staff bao phủ product, category, inventory, order, shipment, return và account
  management theo permission policy.
- Admin management gồm customer lock/unlock, dashboard, revenue/order/product/customer/return
  report, promotion analytics, audit search, Outbox dead-letter redrive và upload reconciliation.
- Read model dùng database projection, `AsNoTracking`, paging có giới hạn và base-currency
  aggregate.

## Tài sản kiểm thử

Repository chứa:

- domain unit test cho state machine, money và business invariant;
- API/application integration test cho auth, authorization, checkout, payment, refund, Outbox,
  reporting và operations;
- deterministic Stripe gateway/Webhook và CurrencyAPI adapter test;
- SQL Server test cho locking, concurrency, constraint, migration, backup/restore dữ liệu nghiệp vụ
  quan trọng và performance;
- architecture, OpenAPI compatibility, deployment security và observability contract test;
- CI workflow cho restore audit, formatting, Release build, migration, coverage và SQL test;
- Docker smoke, migration artifact, recovery, performance và release packaging script.

Sự hiện diện của test chỉ là bằng chứng implementation. Số lượng pass/fail hiện tại, Docker result,
external provider check và release recommendation được ghi riêng sau lần xác minh readiness cuối.

## Ranh giới chủ ý

- Topology triển khai được hỗ trợ là một API instance, một SQL Server và persistent product-image
  storage.
- Stripe, CurrencyAPI và SMTP bị tắt cho đến khi credential được cung cấp từ bên ngoài.
- Email verification được ghi nhận nhưng chưa bắt buộc để login.
- Reconciliation phục hồi payment, không tự phục hồi provider-pending refund.
- Rate limiting và FX cache chạy trong process, không được trình bày như giải pháp phân tán ngang.
- Kết quả performance local/CI là regression baseline, không phải production capacity claim.
