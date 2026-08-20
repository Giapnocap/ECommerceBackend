# Runbook vận hành ECommerceBackend

Runbook này áp dụng cho topology được hỗ trợ hiện tại: một API instance, một SQL Server và storage
bền vững cho ảnh sản phẩm/Data Protection keys. Multi-instance cần xử lý các giới hạn trong
`docs/LIMITATIONS.md` trước khi triển khai.

## 1. Trách nhiệm trước production

Owner vận hành phải chốt và ghi vào hệ thống quản lý sự cố:

- RPO/RTO của database và upload;
- lịch backup, thời gian lưu và vị trí bản sao ngoài host;
- người có quyền deploy, rollback, restore, rotate secret và redrive outbox;
- kênh cảnh báo cùng lịch trực;
- staging hostname, production hostname và CORS origin;
- retention theo yêu cầu pháp lý/nghiệp vụ.

Repository không tự đặt các giá trị này vì chúng phụ thuộc cam kết kinh doanh và hạ tầng.

## 2. Cấu hình bắt buộc

Secret chỉ lấy từ environment variable hoặc secret store. Không tạo `appsettings.Production.json`
có secret trong source/publish directory.

| Nhóm | Biến/cấu hình chính | Yêu cầu |
|---|---|---|
| SQL Server | `ConnectionStrings__Default` | Database riêng; production bật TLS, không dùng credential mặc định. |
| JWT | `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | Key ngẫu nhiên đủ mạnh; issuer/audience đúng client. |
| Host/CORS | `AllowedHosts`, `Cors__AllowedOrigins__0` | Allowlist hostname/origin cụ thể, HTTPS ở staging/production. |
| Reverse proxy | `ReverseProxy__Enabled`, `KnownProxies` hoặc `KnownNetworks` | Chỉ tin proxy/segment thực tế; không tin mọi `X-Forwarded-*`. |
| Data Protection | `DataProtection__KeysPath` | Đường dẫn tuyệt đối trên volume bền vững, có backup và quyền ghi của API. |
| Stripe | `Payments__Stripe__SecretKey`, `PublishableKey`, `WebhookSecret` | Chỉ test key ở staging; live key chỉ ở production secret store. |
| FX | `Pricing__ExchangeRates__ApiKey` | Bắt buộc nếu bật USD/EUR qua provider. |
| SMTP | host, username, password, from address | `EnableSsl=true` ở staging/production. |
| OTLP | `Observability__Otlp__Enabled`, `Endpoint` | Endpoint nội bộ tin cậy; không đặt credential trong URL. |

Dùng `appsettings.Staging.example.json` hoặc `appsettings.Production.example.json` làm schema tham
chiếu. Cả hai template cố ý không chứa credential dùng được.

## 3. Pre-deployment checklist

1. Working tree/release commit đã được review; CI của đúng commit xanh.
2. `dotnet restore` không có advisory mức bị chặn.
3. Format, Release build, unit/integration/SQL tests và coverage gate đạt.
4. `dotnet-ef migrations has-pending-model-changes` báo không có model drift.
5. `MigrationArtifacts/migration-manifest.json` và SHA-256 của hai SQL script hợp lệ.
6. Release ZIP qua `scripts/VerifyReleasePackage.ps1 -SmokeTest`.
7. Backup gần nhất đã hoàn tất và restore drill còn trong thời hạn vận hành cho phép.
8. Kiểm tra dung lượng SQL/upload/log/key volume.
9. Staging đã chạy migration, smoke flow và external provider bằng credential test.
10. Có người trực theo dõi health, error rate, SQL latency, outbox và payment sau deploy.

Không deploy khi thiếu backup có thể restore, chưa biết rollback target hoặc migration chứa thay đổi
không tương thích với binary đang chạy.

## 4. Tạo và xác minh artifact

```powershell
./scripts/BuildMigrationArtifacts.ps1 `
  -OutputDirectory ./MigrationArtifacts `
  -DotNetEfPath ./.tools/dotnet-ef.exe

./scripts/BuildReleasePackage.ps1 `
  -OutputDirectory ./ReleasePackage `
  -MigrationArtifactsDirectory ./MigrationArtifacts `
  -SourceRevision <commit-sha>

./scripts/VerifyReleasePackage.ps1 `
  -ReleaseDirectory ./ReleasePackage `
  -SmokeTest
```

Lưu ZIP, file SHA-256, release manifest và migration manifest cùng release. Không tái sử dụng output
directory cũ.

## 5. Quy trình deploy

### 5.1 Backup và migration

1. Dừng job ghi dữ liệu ngoài API nếu có; giữ API version cũ phục vụ cho đến khi chiến lược
   compatibility cho phép chuyển.
2. Tạo full backup SQL Server và xác minh backup bằng `RESTORE VERIFYONLY`.
3. Ghi lại latest migration hiện tại từ `dbo.__EFMigrationsHistory`.
4. Kiểm tra checksum `database/migrate-up.sql` theo migration manifest.
5. Chạy script bằng account migration riêng, có timeout và fail-on-error:

```powershell
sqlcmd -S <server> -d <database> -E -C -b -f 65001 `
  -i ./database/migrate-up.sql
```

6. Xác nhận latest migration đúng manifest và các health query cơ bản chạy được.

Compose local/CI dùng service `migrate` riêng và chỉ khởi động `api` khi migration exit 0. API không
được xem là công cụ thay thế cho quy trình backup/rollback production.

### 5.2 Deploy API

1. Đặt secret/config qua deployment platform.
2. Mount volume upload, Data Protection keys và log với quyền ghi cho user non-root.
3. Khởi động binary/image mới.
4. Probe `/health/live`; sau đó `/health/ready`.
5. Chỉ đưa instance vào load balancer khi readiness trả HTTP 200.
6. Chạy smoke: product list, login test account, một protected GET và Admin
   `/health/details`.
7. Theo dõi ít nhất error rate, SQL latency, worker health, outbox backlog và payment failure trong
   cửa sổ theo chính sách deploy của đội.

## 6. Rollback

### 6.1 Rollback API

1. Loại instance lỗi khỏi traffic.
2. Giữ log, correlation ID và release manifest của bản lỗi.
3. Deploy lại image/artifact trước đó với cùng config schema tương thích.
4. Kiểm tra live/ready và smoke trước khi nhận traffic.

### 6.2 Rollback database

`rollback-last.sql` chỉ lùi đúng migration cuối được ghi trong manifest. Trước khi chạy:

1. Đọc script và kiểm tra khả năng mất cột/data.
2. Xác nhận binary cũ tương thích với schema rollback.
3. Backup database tại thời điểm sự cố.
4. Dừng write traffic nếu migration không online-safe.
5. Chạy script với `sqlcmd -b`, kiểm tra `__EFMigrationsHistory`, constraint và dữ liệu.

Migration snapshot người nhận cố ý từ chối rollback khi có dữ liệu không thể bảo toàn. Khi rollback
schema không an toàn, phục hồi full backup/point-in-time vào database mới rồi chuyển connection
string; không ép xóa dữ liệu để script chạy.

## 7. Backup và restore

### Backup tối thiểu

- SQL Server: full backup và transaction-log backup theo RPO đã chốt.
- Product upload volume: snapshot/copy nhất quán.
- Data Protection key volume: backup được mã hóa, quyền truy cập hạn chế.
- Release/migration manifests và config không chứa secret.

Log ứng dụng không thay thế database backup. Backup lưu trên cùng host/volume không được tính là
bản sao phục hồi thảm họa.

### Restore drill

1. Restore vào server/database cô lập, không ghi đè production.
2. Chạy `DBCC CHECKDB` theo quy trình DBA.
3. Xác minh latest migration và marker business đã chọn trước backup.
4. Khởi động API cô lập bằng bản release tương ứng.
5. Kiểm tra login, product, order detail, payment/refund history và inventory ledger.
6. Ghi thời gian restore thực tế, data loss window và sai lệch so với RPO/RTO.
7. Xóa dữ liệu drill theo chính sách sau khi lưu bằng chứng.

Repository có test `SqlServerRecoveryIntegration` cho backup/restore schema + committed marker; test
này không thay thế restore drill của backup production.

## 8. Health và monitoring

- `/health/live`: tiến trình API.
- `/health/ready`: SQL Server, storage và worker bắt buộc.
- `/health/details`: chi tiết, chỉ role `Admin`.
- Dashboard/alert baseline: `docs/MONITORING.md`.

Khi readiness lỗi, xem tên check trước khi restart. Restart liên tục không sửa được credential sai,
SQL blocking, storage read-only, outbox backlog hoặc provider outage.

## 9. Playbook sự cố

### Database unavailable/slow

1. Loại API khỏi traffic nếu readiness fail.
2. Kiểm tra connectivity, certificate, login, disk, blocking, deadlock và SQL resource pressure.
3. Tra `database.command.duration`, request trace và correlation ID.
4. Không tăng command timeout trước khi xác định query/blocking.
5. Sau phục hồi, kiểm tra order/payment/inventory invariant và worker backlog.

### Outbox backlog/dead letter

1. Xem `/health/details`, `outbox.backlog.*` và SMTP/provider error.
2. Sửa credential/connectivity/template trước.
3. Admin xem `GET /api/v1/operations/outbox/dead-letters`.
4. Redrive từng message bằng `POST .../{id}/redrive`; delivery là at-least-once.
5. Theo dõi `processed`, `failed`, `dead_lettered` và oldest age về bình thường.

Không redrive hàng loạt khi chưa xác minh downstream chấp nhận `Message-ID` lặp.

### Stripe webhook/reconciliation

1. Kiểm tra endpoint HTTPS, Stripe delivery log và timestamp/signature rejection.
2. Không sửa raw body hoặc `Stripe-Signature` tại proxy.
3. Xác minh amount, currency, payment ID và provider transaction ID trước retry.
4. Webhook cùng event ID có thể gửi lại an toàn; không tạo event ID mới thủ công.
5. Nếu webhook bị lỡ, theo dõi reconciliation health/counter và payment stale batch.
6. Đối chiếu Stripe Dashboard với payment, status history và audit trước khi can thiệp.

### Refund stuck/failed

1. Tra refund idempotency key, provider refund ID, status, attempt count và failure code.
2. Kiểm tra tổng completed + pending refund không vượt payment amount.
3. Không tạo reference mới chỉ để vượt qua retry; retry cùng idempotency key sau khi xử lý nguyên
   nhân.
4. Reconciliation hiện không tự sửa provider-pending refund; xử lý theo Stripe Dashboard và bằng
   chứng audit.

### FX provider unavailable

1. Kiểm tra timeout/credential/quota của CurrencyAPI.
2. Xác minh tuổi cache so với `CacheMinutes` và `MaxStaleMinutes`.
3. Không tự nhập rate giả để tiếp tục checkout.
4. VND vẫn dùng rate 1; có thể tạm bỏ USD/EUR khỏi supported currencies bằng change được review.

### Upload/storage

1. Kiểm tra readiness, mount, permission, free space và inode.
2. Không xóa file trực tiếp trước khi đối chiếu DB.
3. Admin chạy `/api/v1/operations/uploads/reconcile` ở preview trước; chỉ apply sau khi review grace
   period và danh sách orphan.
4. Restore upload volume nếu file business bị mất; DB restore riêng không khôi phục ảnh.

### Auth/JWT incident

1. Phân biệt credential stuffing, token reuse, clock skew và JWT key mismatch.
2. Khóa account/thu hồi session bằng API quản trị; không xóa refresh row tùy tiện.
3. Nếu JWT key lộ, rotate theo mục 10 và chấp nhận toàn bộ access token cũ mất hiệu lực.
4. Tìm audit/login outcome nhưng không log password/token.

## 10. Rotation secret

### JWT signing key

Ứng dụng hiện dùng một symmetric key. Rotation là cutover, không có dual-key window:

1. Thông báo việc access token hiện tại sẽ mất hiệu lực.
2. Cập nhật secret store và restart có kiểm soát.
3. Xác minh login/refresh mới; theo dõi 401 tăng tạm thời.
4. Thu hồi/rotate key cũ trong secret manager.

### Stripe/FX/SMTP/database

1. Tạo credential mới với least privilege.
2. Cập nhật staging và xác minh trước.
3. Cập nhật production secret store rồi restart/rollout.
4. Chạy health/smoke tương ứng.
5. Thu hồi credential cũ sau khi xác nhận không còn consumer dùng.

Webhook secret rotation cần phối hợp Stripe endpoint; không đổi secret trong app trước khi provider
phát chữ ký bằng secret mới.

## 11. Data retention và vận hành định kỳ

- Luôn chạy `POST /api/v1/operations/data-retention` với `applyChanges=false` trước.
- Batch size và retention days phải qua review; không dùng retention để xử lý incident tức thời.
- Theo dõi lock contention và số record thay đổi.
- Kiểm tra dead letter, payment reconciliation, order expiration và backup job mỗi ngày theo lịch
  vận hành.
- Chạy restore drill, dependency audit và performance regression theo chu kỳ release/rủi ro.

## 12. Bằng chứng sau deploy

Lưu cùng release ticket:

- commit SHA, image digest/release checksum;
- migration manifest và migration trước/sau;
- backup ID cùng kết quả verify;
- live/ready/smoke result;
- dashboard/error/outbox/payment snapshot sau deploy;
- external provider test IDs không chứa secret;
- quyết định rollback hoặc tiếp tục;
- mọi incident/correlation ID phát sinh.
